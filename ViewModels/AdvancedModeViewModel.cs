﻿using System;
using System.IO;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Reactive.Linq;
using Proggy.Core;
using Proggy.Models;
using Proggy.Infrastructure;
using Proggy.Infrastructure.Events;
using Proggy.ViewModels.CollectionItems;
using ReactiveUI;
using Avalonia.Threading;
using NAudio.Wave;

namespace Proggy.ViewModels
{
    public class AdvancedModeViewModel : ViewModelBase
    {
        public const int MaxRowsOrCols = 5;

        public ObservableCollection<ClickTrackGridItem> Items { get; }

        public Action<int> ScrollToBar { get; set; }

        public bool Loop
        {
            get => loop;
            set => this.RaiseAndSetIfChanged(ref loop, value);
        }
        public bool Precount
        {
            get => precount;
            set => this.RaiseAndSetIfChanged(ref precount, value);
        }

        public string TrackName
        {
            get => trackName;
            set => this.RaiseAndSetIfChanged(ref trackName, value);
        }

        public bool CanUseContextMenu
        {
            get => canUseContextMenu && Items.Count > 2;
            set => this.RaiseAndSetIfChanged(ref canUseContextMenu, value);
        }

        public bool IsSelecting => selection?.IsSelecting ?? false;

        public GlobalControlsViewModel GlobalControls => globalControls;

        private readonly GlobalControlsViewModel globalControls;
        private IDisposable playbackChangedSub;

        private bool loop;
        private bool precount;
        private bool canUseContextMenu;
        private bool pendingChanges;
        private int currentItemIndex;
        private string trackName;
        private AccurateTimer timer;
        private Selection selection;

        public AdvancedModeViewModel()
        {
            globalControls = new GlobalControlsViewModel(BuildClickTrackAsync, MetronomeMode.Advanced);

            Items = new ObservableCollection<ClickTrackGridItem>();
            InitializeTrack();

            Loop = true;

            canUseContextMenu = true;

            playbackChangedSub = MessageBus.Current.Listen<MetronomePlaybackStateChanged>()
                                                   .Subscribe(OnMetronomePlaybackStateChanged);

            timer = new AccurateTimer(UpdateCurrentBar);

            SetNewTrackName();
        }

        public async void OnItemClicked(BarInfoGridItem item)
        {
            if (AudioPlayer.Instance.IsPlaying)
                return;

            pendingChanges = true;

            //Add button pressed
            if(item is null)
            {
                var lastItem = (BarInfoGridItem)Items[Items.Count - 2];
                Items.Insert(Items.Count - 1, new BarInfoGridItem(lastItem.BarInfo));
            }
            else
            {
                var result = await WindowNavigation.ShowDialogAsync(() => 
                {
                    return new TimeSignatureDialogViewModel(item.BarInfo);
                });

                if (result.IsConfirm)
                    item.BarInfo = result.BarInfo;
            }
        }

        public void Delete(BarInfoGridItem item)
        {
            if (selection is null || selection.IsSelecting)
            {
                DeselectAll();
                Items.Remove(item);
            }
            else
            {
                for (var i = selection.Start; i <= selection.End; i++)
                    Items.RemoveAt(selection.Start);
                
                if (Items.Count < 2)
                    Items.Insert(0, new BarInfoGridItem(BarInfo.Default));

                selection = null;
            }

            pendingChanges = true;
        }

        public async Task Save()
        {
            pendingChanges = false;

            var infos = Items.OfType<BarInfoGridItem>().Select(x => x.BarInfo).ToArray();
            
            try
            {
                await ClickTrackFile.Save(infos, TrackName);
            }
            catch(Exception e)
            {
                await WindowNavigation.ShowErrorMessageAsync(e);
            }
        }

        public async void Open()
        {
            globalControls.Stop();

            var result = await WindowNavigation.ShowDialogAsync(() => 
            {
                return new OpenClickTrackDialog(ClickTrackFile.Enumerate());
            });

            if(result.IsConfirm)
            {
                selection = null;

                try
                {
                    var track = await ClickTrackFile.Load(result.SelectedTrack);

                    Items.Clear();

                    foreach (var info in track)
                        Items.Add(new BarInfoGridItem(info));

                    Items.Add(new AddButtonGridItem());

                    TrackName = result.SelectedTrack;
                }
                catch(Exception e)
                {
                    await WindowNavigation.ShowErrorMessageAsync(e);
                }
            }
        }

        public async void New()
        {
            if (AudioPlayer.Instance.IsPlaying)
                return;

            await PromptSave();

            pendingChanges = false;
            selection = null;

            Items.Clear();
            InitializeTrack();
            SetNewTrackName();
        }

        public void BeginSelection(BarInfoGridItem item)
        {
            DeselectAll();

            item.IsSelected = true;

            selection = new Selection();
            selection.Begin(Items.IndexOf(item));

            this.RaisePropertyChanged(nameof(IsSelecting));
        }

        public void EndSelection(BarInfoGridItem item)
        {
            selection.Finalize(Items.IndexOf(item));
            
            if(selection.Start == selection.End)
            {
                item.IsSelected = false;
                selection = null;

                return;
            }

            for(var i = selection.Start; i <= selection.End; i++)
            {
                var bar = (BarInfoGridItem)Items[i];
                bar.IsSelected = true;
            }

            this.RaisePropertyChanged(nameof(IsSelecting));
        }

        public override void OnClosing()
        {
            playbackChangedSub.Dispose();
            globalControls.OnClosing();
        }

        public async Task PromptSave()
        {
            if (!pendingChanges)
                return;

            var result = await WindowNavigation.PromptAsync("Do you wish to save your track?", "Save?");

            if (result.Result == DialogAction.OK)
                await Save();
        }

        private async Task<ISampleProvider> BuildClickTrackAsync()
        {
            var infos = Items.OfType<BarInfoGridItem>().Select(x => x.BarInfo).ToArray();
            var settings = await UserSettings.Get();

            return await ClickTrackBuilder.BuildClickTrackAsync(infos, settings.ClickSettings, precount, loop);
        }

        private void OnMetronomePlaybackStateChanged(MetronomePlaybackStateChanged msg)
        {
            if (msg.State == MetronomePlaybackState.Playing)
            {
                Items.RemoveAt(Items.Count - 1); //Remove add button

                CanUseContextMenu = false;

                DeselectAll();

                if (Items.Count > 1)
                {
                    currentItemIndex = 0;

                    if (precount)
                    {
                        var first = (BarInfoGridItem)Items.First();
                        timer.Interval = new BarInfo(first.BarInfo.Tempo, 4, 4).Interval * 4;
                    }
                    else
                        timer.Interval = 0;

                    timer.Start();
                }
            }
            else
            {
                Items.Add(new AddButtonGridItem());

                CanUseContextMenu = true;

                if (Items.Count > 1)
                {
                    timer.Stop();
                    DeselectAll();
                }
            }
        }

        private async void UpdateCurrentBar()
        {
            var current = (BarInfoGridItem)Items[currentItemIndex];
            current.IsSelected = true;

            if (currentItemIndex > 0)
            {
                var previous = (BarInfoGridItem)Items[currentItemIndex - 1];
                previous.IsSelected = false;
            }
            else if (currentItemIndex == 0)
            {
                var lastItem = (BarInfoGridItem)Items[Items.Count - 1];
                lastItem.IsSelected = false;
            }

            if (currentItemIndex == Items.Count - 1)
                currentItemIndex = 0;
            else
                currentItemIndex++;

            timer.Interval = current.BarInfo.Interval * current.BarInfo.Beats;

            //This should be done last
            if(currentItemIndex % MaxRowsOrCols == 1)
                await Dispatcher.UIThread.InvokeAsync(() => ScrollToBar(currentItemIndex));
        }

        private void DeselectAll()
        {
            foreach (var item in Items.OfType<BarInfoGridItem>())
                item.IsSelected = false;

            selection = null;
        }

        private void SetNewTrackName()
        {
            const string newTrack = "New Track";

            var names = ClickTrackFile.Enumerate()
                .Where(x => x.StartsWith(newTrack))
                .ToArray();

            if (names.Length > 0)
            {
                var counter = names.Length;
                var name = string.Empty;

                do
                {
                    counter++;
                    name = $"{newTrack} {counter}";
                }
                while (File.Exists(Path.Combine(ClickTrackFile.FolderPath, $"{name}{ClickTrackFile.FileExtension}")));

                TrackName = name;
            }
            else
                TrackName = newTrack;
        }

        private void InitializeTrack()
        {
            Items.Add(new BarInfoGridItem(BarInfo.Default));
            Items.Add(new AddButtonGridItem());
        }
    }
}

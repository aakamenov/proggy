﻿using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using Proggy.Core;
using Proggy.Infrastructure;
using Proggy.Infrastructure.Events;
using Proggy.Models;
using Proggy.Infrastructure.Commands;
using Proggy.ViewModels.CollectionItems;
using ReactiveUI;
using NAudio.Wave;

namespace Proggy.ViewModels
{
    public class GlobalControlsViewModel : ViewModelBase
    {
        public ListItem<MetronomeMode>[] Modes { get; }
        public ListItem<MetronomeMode> SelectedMode
        {
            get => selectedMode;
            set => this.RaiseAndSetIfChanged(ref selectedMode, value);
        }

        public bool CanPlay
        {
            get => canPlay;
            set => this.RaiseAndSetIfChanged(ref canPlay, value);
        }

        public float Volume
        {
            get => volume;
            set => this.RaiseAndSetIfChanged(ref volume, value);
        }

        public string VolumeText => Math.Round(volume * 100).ToString();

        public string PlayButtonText
        {
            get => playButtonText;
            set => this.RaiseAndSetIfChanged(ref playButtonText, value);
        }

        public Command ToggleCommand { get; }
        public Command SettingsCommand { get; }

        private string playButtonText;
        private float volume;
        private bool canPlay;
        private ListItem<MetronomeMode> selectedMode;
        private readonly Func<Task<ISampleProvider>> buildClickTrack;

        public GlobalControlsViewModel(Func<Task<ISampleProvider>> buildClickTrack, MetronomeMode selectedMode)
        {
            this.buildClickTrack = buildClickTrack;

            Modes = new ListItem<MetronomeMode>[] 
            {
                new ListItem<MetronomeMode>("Basic", MetronomeMode.Basic),
                new ListItem<MetronomeMode>("Advanced", MetronomeMode.Advanced)
            };
            SelectedMode = Modes.First(x => selectedMode == x.Value);

            playButtonText = "Play";
            canPlay = true;
            volume = AudioPlayer.Instance.Volume;

            this.ObservableForProperty(x => x.SelectedMode)
                .Subscribe(x => 
                {
                    Stop();
                    MessageBus.Current.SendMessage(new ModeChanged(x.Value.Value)); 
                });

            this.WhenAnyValue(x => x.Volume)
                .Subscribe(x => 
                {
                    AudioPlayer.Instance.Volume = x;
                    this.RaisePropertyChanged(nameof(VolumeText)); 
                });

            AudioPlayer.Instance.PlaybackStopped += OnPlaybackStopped;

            ToggleCommand = new Command(Toggle);
            SettingsCommand = new Command(Settings);
        }

        public override void OnClosing()
        {
            AudioPlayer.Instance.PlaybackStopped -= OnPlaybackStopped;
        }

        public async Task Play()
        {
            if (AudioPlayer.Instance.IsPlaying)
                return;

            CanPlay = false;
            AudioPlayer.Instance.PlaySound(await buildClickTrack());
            MessageBus.Current.SendMessage(new MetronomePlaybackStateChanged(MetronomePlaybackState.Playing));
            CanPlay = true;

            PlayButtonText = "Stop";
        }

        public void Stop()
        {
            if (!AudioPlayer.Instance.IsPlaying)
                return;

            AudioPlayer.Instance.Stop();
            MessageBus.Current.SendMessage(new MetronomePlaybackStateChanged(MetronomePlaybackState.Stopped));

            PlayButtonText = "Play";
        }

        private async void Toggle()
        {
            if (AudioPlayer.Instance.IsPlaying)
                Stop();
            else
                await Play();
        }

        
        private async void Settings()
        {
            Stop();

            var settings = await UserSettings.Get();

            var result = await WindowNavigation.OpenWindow(() => new SettingsWindowViewModel(settings));

            await result.UserSettings.Save();
        }
        

        private async void OnPlaybackStopped(object sender, EventArgs e)
        {
            await Application.Current.Dispatcher.BeginInvoke(() =>
            {
                MessageBus.Current.SendMessage(new MetronomePlaybackStateChanged(MetronomePlaybackState.Stopped));
            });

            PlayButtonText = "Play";
        }
    }
}

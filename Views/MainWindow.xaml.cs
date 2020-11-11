﻿using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Proggy.ViewModels;

namespace Proggy.Views
{
    public class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            DataContext = new MainWindowViewModel();
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
    }
}

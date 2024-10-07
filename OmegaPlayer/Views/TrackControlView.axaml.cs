using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using OmegaPlayer.ViewModels;
using System;

namespace OmegaPlayer.Views
{

    public partial class TrackControlView : UserControl
    {
        private TrackControlViewModel _trackControlViewModel;

        public TrackControlView()
        {
            InitializeComponent();
            ViewModelLocator.AutoWireViewModel(this);

            _trackControlViewModel = (TrackControlViewModel)this.DataContext;

            this.Loaded += OnLoaded;
            this.Unloaded += OnUnloaded;
            MainGrid.PropertyChanged += MainGrid_PropertyChanged;
        }

        private void OnLoaded(object? sender, EventArgs e)
        {
            UpdateTrackTitleMaxWidth();
        }

        private void MainGrid_PropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == Grid.BoundsProperty)
            {
                UpdateTrackTitleMaxWidth();
            }
        }

        private void UpdateTrackTitleMaxWidth()
        {
            // view's usercontrol width
            double minWidth = this.MinWidth;
            double currentWidth = this.Bounds.Width;
            double percentage = 0.3; // standard percentage

            if (currentWidth > minWidth)
            {
                for (int i = (int)minWidth; i < currentWidth; i += 100)
                {
                    percentage += 0.01; // increases percentage per every 100 width bigger than 850 (Minimum resolution)
                }
            }

            double widthPercentage = currentWidth * percentage;
            var textMaxWidth = widthPercentage - 127; // 127 is the width reserved for thumbnail

            _trackControlViewModel.TrackTitleMaxWidth = textMaxWidth > 0 ? textMaxWidth : 0;
        }

        private void OnUnloaded(object sender, EventArgs e)
        {
            MainGrid.PropertyChanged -= MainGrid_PropertyChanged;
            this.Loaded -= OnLoaded;
            this.Unloaded -= OnUnloaded;
        }
    }
}
﻿using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using System;

namespace OmegaPlayer.UI.Helpers
{
    public class SelectableTextBlock : TextBox
    {
        private bool _isDisposed = false;

        public SelectableTextBlock()
        {
            // Make it look and behave more like a TextBlock
            IsReadOnly = true;
            BorderThickness = new Thickness(0);
            Background = Brushes.Transparent;
            Padding = new Thickness(0);

            // Disable caret
            CaretIndex = 0;
            CaretBrush = Brushes.Transparent;

            // Allow selection
            IsUndoEnabled = false;

            // Improve appearance
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;

            // Setup keyboard shortcuts for common actions
            AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (_isDisposed) return;

            base.OnPointerPressed(e);

            // Focus the control when clicked
            Focus();
            e.Handled = true;
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (_isDisposed) return;

            // Handle Ctrl+C for copy
            if (e.Key == Key.C && e.KeyModifiers == KeyModifiers.Control)
            {
                Copy();
                e.Handled = true;
            }

            // Handle Ctrl+A for select all
            if (e.Key == Key.A && e.KeyModifiers == KeyModifiers.Control)
            {
                SelectAll();
                e.Handled = true;
            }
        }

        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _isDisposed = true;

            // Remove event handler
            RemoveHandler(KeyDownEvent, OnKeyDown);

            base.OnDetachedFromVisualTree(e);
        }

        protected override Type StyleKeyOverride => typeof(TextBox);
    }
}
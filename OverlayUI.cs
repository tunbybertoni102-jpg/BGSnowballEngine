using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using HearthstoneDeckTracker.API;
using HearthstoneDeckTracker.Hearthstone;

namespace BGSnowballEngine
{
    public class OverlayUI
    {
        private Canvas _canvas;
        private List<Rectangle> _highlights = new List<Rectangle>();

        public OverlayUI()
        {
            _canvas = Core.OverlayCanvas;
        }

        public void UpdateHighlights(Dictionary<Card, double> scoredCards)
        {
            ClearOverlay();
            if (scoredCards.Count == 0) return;

            var bestCardEntry = scoredCards.OrderByDescending(x => x.Value).FirstOrDefault();
            if (bestCardEntry.Value < 15.0) return;

            Card bestCard = bestCardEntry.Key;
            DrawRectangleForCard(bestCard);
        }

        private void DrawRectangleForCard(Card card)
        {
            Rectangle highlight = new Rectangle
            {
                Width = 150,
                Height = 200,
                Stroke = Brushes.LimeGreen,
                StrokeThickness = 4,
                Fill = new SolidColorBrush(Color.FromArgb(50, 0, 255, 0))
            };

            Canvas.SetLeft(highlight, SystemParameters.PrimaryScreenWidth / 2 - 75);
            Canvas.SetTop(highlight, SystemParameters.PrimaryScreenHeight / 2 - 100);

            _canvas.Children.Add(highlight);
            _highlights.Add(highlight);
        }

        public void ClearOverlay()
        {
            foreach (var rect in _highlights)
            {
                _canvas.Children.Remove(rect);
            }
            _highlights.Clear();
        }
    }
}

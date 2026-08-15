using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Hearthstone;

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
            if (_canvas == null) return;

            // Перенаправляем отрисовку в главный графический поток WPF
            _canvas.Dispatcher.Invoke(() =>
            {
                ClearOverlay();
                if (scoredCards == null || scoredCards.Count == 0) return;

                var bestCardEntry = scoredCards.OrderByDescending(x => x.Value).FirstOrDefault();
                
                // Снизили порог, чтобы подсветка гарантированно срабатывала при совпадении карты
                if (bestCardEntry.Value <= 0) return;

                DrawRectangleForCard(bestCardEntry.Key);
            });
        }

        private void DrawRectangleForCard(Card card)
        {
            Rectangle highlight = new Rectangle
            {
                Width = 160,
                Height = 220,
                Stroke = Brushes.LimeGreen,
                StrokeThickness = 4,
                Fill = new SolidColorBrush(Color.FromArgb(60, 0, 255, 0)),
                IsHitTestVisible = false // Пропускаем клики мыши сквозь рамку
            };

            // Рисуем рамку по центру экрана
            Canvas.SetLeft(highlight, (SystemParameters.PrimaryScreenWidth / 2) - 80);
            Canvas.SetTop(highlight, (SystemParameters.PrimaryScreenHeight / 2) - 110);

            _canvas.Children.Add(highlight);
            _highlights.Add(highlight);
        }

        public void ClearOverlay()
        {
            if (_canvas == null) return;

            _canvas.Dispatcher.Invoke(() =>
            {
                foreach (var rect in _highlights)
                {
                    _canvas.Children.Remove(rect);
                }
                _highlights.Clear();
            });
        }
    }
}

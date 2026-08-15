using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Hearthstone_Deck_Tracker.API;

namespace BGSnowballEngine
{
    public class ScoredSlot
    {
        public int SlotIndex { get; set; }
        public int TotalSlots { get; set; }
        public double Score { get; set; }
    }

    public class OverlayUI
    {
        private Canvas _canvas;
        private List<UIElement> _dynamicElements = new List<UIElement>();

        private Border _buildPanel;
        private TextBlock _buildTitle;
        private TextBlock _buildSubtitle;
        private TextBlock _powerMeter;

        public OverlayUI()
        {
            _canvas = Core.OverlayCanvas;
            InitRightSidePanel();
        }

        private void InitRightSidePanel()
        {
            if (_canvas == null) return;

            _canvas.Dispatcher.Invoke(() =>
            {
                _buildPanel = new Border
                {
                    Width = 200,
                    Background = new SolidColorBrush(Color.FromArgb(225, 12, 16, 22)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(220, 212, 175, 55)),
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 8, 10, 8),
                    IsHitTestVisible = false,
                    Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 12,
                        ShadowDepth = 2,
                        Opacity = 0.85
                    }
                };

                var stack = new StackPanel();

                var header = new TextBlock
                {
                    Text = "🎯 ТЕКУЩАЯ СБОРКА",
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(200, 200, 200, 200)),
                    Margin = new Thickness(0, 0, 0, 4)
                };

                _buildTitle = new TextBlock
                {
                    Text = "Анализ стола...",
                    FontSize = 14,
                    FontWeight = FontWeights.ExtraBold,
                    Foreground = Brushes.Gold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };

                _buildSubtitle = new TextBlock
                {
                    Text = "Ожидание карт",
                    FontSize = 11,
                    Foreground = Brushes.WhiteSmoke,
                    Margin = new Thickness(0, 2, 0, 6)
                };

                _powerMeter = new TextBlock
                {
                    Text = "⚡ Синергия: 0%",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.LimeGreen
                };

                stack.Children.Add(header);
                stack.Children.Add(_buildTitle);
                stack.Children.Add(_buildSubtitle);
                stack.Children.Add(_powerMeter);

                _buildPanel.Child = stack;

                UpdatePanelPosition();
                _canvas.Children.Add(_buildPanel);
            });
        }

        private void UpdatePanelPosition()
        {
            double screenW = _canvas.ActualWidth > 0 ? _canvas.ActualWidth : SystemParameters.PrimaryScreenWidth;
            double screenH = _canvas.ActualHeight > 0 ? _canvas.ActualHeight : SystemParameters.PrimaryScreenHeight;

            Canvas.SetLeft(_buildPanel, screenW - 225);
            Canvas.SetTop(_buildPanel, screenH * 0.22);
        }

        public void UpdateBuildStatus(ArchetypeSummary summary)
        {
            if (_canvas == null || _buildPanel == null) return;

            _canvas.Dispatcher.Invoke(() =>
            {
                UpdatePanelPosition();
                _buildTitle.Text = summary.Name;
                _buildSubtitle.Text = summary.Subtitle;
                _powerMeter.Text = $"⚡ Синергия: {summary.SynergyPower}%";
                _powerMeter.Foreground = summary.SynergyPower >= 50 ? Brushes.Gold : Brushes.LimeGreen;
            });
        }

        public void UpdateTavernHighlights(List<ScoredSlot> items)
        {
            if (_canvas == null) return;

            _canvas.Dispatcher.Invoke(() =>
            {
                ClearHighlights();

                if (items == null || items.Count == 0) return;

                // Подсвечиваем любые совпавшие карты со скором >= 2.0
                var targets = items.Where(x => x.Score >= 2.0).ToList();
                if (targets.Count == 0) return;

                double screenW = _canvas.ActualWidth > 0 ? _canvas.ActualWidth : SystemParameters.PrimaryScreenWidth;
                double screenH = _canvas.ActualHeight > 0 ? _canvas.ActualHeight : SystemParameters.PrimaryScreenHeight;

                double cardW = screenW * 0.072;
                double cardH = screenH * 0.128;
                double stepX = screenW * 0.0885;
                double tavernY = screenH * 0.288;
                double centerX = screenW / 2.0;

                foreach (var item in targets)
                {
                    double offsetFromCenter = (item.SlotIndex - ((item.TotalSlots - 1) / 2.0)) * stepX;
                    double posX = centerX + offsetFromCenter - (cardW / 2.0);
                    double posY = tavernY - (cardH / 2.0);

                    bool isHighPriority = item.Score >= 5.0;
                    Brush strokeColor = isHighPriority ? Brushes.Gold : Brushes.LimeGreen;
                    Color glowFill = isHighPriority ? Color.FromArgb(70, 255, 215, 0) : Color.FromArgb(50, 0, 255, 0);

                    // Овал строго по форме существа в таверне
                    Rectangle contour = new Rectangle
                    {
                        Width = cardW,
                        Height = cardH,
                        Stroke = strokeColor,
                        StrokeThickness = 3.5,
                        RadiusX = 18,
                        RadiusY = 18,
                        Fill = new SolidColorBrush(glowFill),
                        IsHitTestVisible = false,
                        Effect = new DropShadowEffect
                        {
                            Color = isHighPriority ? Colors.Gold : Colors.LimeGreen,
                            BlurRadius = 8,
                            ShadowDepth = 0,
                            Opacity = 0.85
                        }
                    };

                    Canvas.SetLeft(contour, posX);
                    Canvas.SetTop(contour, posY);
                    _canvas.Children.Add(contour);
                    _dynamicElements.Add(contour);

                    // Компактный бейдж рейтинга
                    TextBlock badge = new TextBlock
                    {
                        Text = $"★ {item.Score:0.#}",
                        FontSize = 11,
                        FontWeight = FontWeights.ExtraBold,
                        Foreground = Brushes.White,
                        Background = new SolidColorBrush(Color.FromArgb(220, 10, 10, 10)),
                        Padding = new Thickness(5, 1, 5, 1),
                        IsHitTestVisible = false
                    };

                    Canvas.SetLeft(badge, posX + (cardW / 2.0) - 18);
                    Canvas.SetTop(badge, posY - 20);
                    _canvas.Children.Add(badge);
                    _dynamicElements.Add(badge);
                }
            });
        }

        public void ClearHighlights()
        {
            if (_canvas == null) return;

            _canvas.Dispatcher.Invoke(() =>
            {
                foreach (var el in _dynamicElements)
                {
                    _canvas.Children.Remove(el);
                }
                _dynamicElements.Clear();
            });
        }

        public void ClearOverlay()
        {
            if (_canvas == null) return;

            _canvas.Dispatcher.Invoke(() =>
            {
                ClearHighlights();
                if (_buildPanel != null)
                {
                    _canvas.Children.Remove(_buildPanel);
                }
            });
        }
    }
}

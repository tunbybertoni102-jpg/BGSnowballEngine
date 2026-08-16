using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Utility.Extensions;

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

        private double _canvasW;
        private double _canvasH;

        private bool _dragging;
        private Point _dragStart;
        private double _panelLeft;
        private double _panelTop;

        private Border _buildPanel;
        private TextBlock _buildTitle;
        private TextBlock _buildSubtitle;
        private ProgressBar _progressBar;
        private TextBlock _powerMeter;
        private TextBlock _adviceText;

        public OverlayUI()
        {
            _canvas = Core.OverlayCanvas;
            if (_canvas != null)
            {
                // Отслеживаем реальный размер канваса: координаты подсветки
                // должны совпадать с окном игры, а не с размером экрана
                _canvas.SizeChanged += OnCanvasSizeChanged;
            }
            InitRightSidePanel();
        }

        private void OnCanvasSizeChanged(object sender, SizeChangedEventArgs e)
        {
            _canvasW = _canvas.ActualWidth;
            _canvasH = _canvas.ActualHeight;
        }

        private double GetCanvasWidth()
        {
            return _canvas.ActualWidth > 0 ? _canvas.ActualWidth
                : _canvasW > 0 ? _canvasW
                : SystemParameters.PrimaryScreenWidth;
        }

        private double GetCanvasHeight()
        {
            return _canvas.ActualHeight > 0 ? _canvas.ActualHeight
                : _canvasH > 0 ? _canvasH
                : SystemParameters.PrimaryScreenHeight;
        }

        private void InitRightSidePanel()
        {
            if (_canvas == null) return;

            _canvas.Dispatcher.Invoke(() =>
            {
                // Защита от повторной инициализации (перезапуск игры/реконфигурация)
                if (_buildPanel != null) return;

                _buildPanel = new Border
                {
                    Width = 200,
                    Background = new SolidColorBrush(Color.FromArgb(225, 12, 16, 22)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(220, 212, 175, 55)),
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 8, 10, 8),
                    // WPF hit-test внутри окна; клик-сквозной режим самого окна HDT
                    // снимается регистрацией через SetIsOverlayHitTestVisible (ниже)
                    IsHitTestVisible = true,
                    Cursor = Cursors.SizeAll,
                    Effect = new DropShadowEffect
                    {
                        Color = Colors.Black,
                        BlurRadius = 12,
                        ShadowDepth = 2,
                        Opacity = 0.85
                    }
                };

                // КЛЮЧЕВОЕ: окно оверлея HDT постоянно клик-сквозное (WS_EX_TRANSPARENT).
                // Регистрация панели как интерактивной заставляет HDT снимать
                // клик-сквозной режим при наведении курсора (UpdateHoverable, 60 Гц).
                OverlayExtensions.SetIsOverlayHitTestVisible(_buildPanel, true);

                // Отписка перед подпиской — защита от двойных обработчиков при реините
                _buildPanel.MouseLeftButtonDown -= OnPanelMouseDown;
                _buildPanel.MouseLeftButtonDown += OnPanelMouseDown;
                _buildPanel.MouseMove -= OnPanelMouseMove;
                _buildPanel.MouseMove += OnPanelMouseMove;
                _buildPanel.MouseLeftButtonUp -= OnPanelMouseUp;
                _buildPanel.MouseLeftButtonUp += OnPanelMouseUp;
                _buildPanel.LostMouseCapture -= OnPanelLostMouseCapture;
                _buildPanel.LostMouseCapture += OnPanelLostMouseCapture;

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
                    Margin = new Thickness(0, 2, 0, 4)
                };

                _progressBar = new ProgressBar
                {
                    Minimum = 0,
                    Maximum = 100,
                    Value = 0,
                    Height = 4,
                    Foreground = new SolidColorBrush(Color.FromRgb(0xD4, 0xAF, 0x37)),
                    Background = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)),
                    BorderThickness = new Thickness(0),
                    Margin = new Thickness(0, 0, 0, 4)
                };

                _powerMeter = new TextBlock
                {
                    Text = "⚡ Синергия: 0%",
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = Brushes.LimeGreen
                };

                _adviceText = new TextBlock
                {
                    Text = "👉 Ждать: конец хода",
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(240, 212, 175, 55)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 0)
                };

                stack.Children.Add(header);
                stack.Children.Add(_buildTitle);
                stack.Children.Add(_buildSubtitle);
                stack.Children.Add(_progressBar);
                stack.Children.Add(_powerMeter);
                stack.Children.Add(_adviceText);

                _buildPanel.Child = stack;

                UpdatePanelPosition();
                _canvas.Children.Add(_buildPanel);
            });
        }

        private void UpdatePanelPosition()
        {
            double screenW = GetCanvasWidth();
            double screenH = GetCanvasHeight();

            Canvas.SetLeft(_buildPanel, screenW - 225);
            Canvas.SetTop(_buildPanel, screenH * 0.22);
        }

        /// <summary>
        /// Не сбрасываем позицию при каждом обновлении (панель перетаскивается!),
        /// а только возвращаем её в границы канваса, если та вышла за них.
        /// </summary>
        private void ClampPanelToBounds()
        {
            if (_buildPanel == null) return;

            double left = Canvas.GetLeft(_buildPanel);
            double top = Canvas.GetTop(_buildPanel);
            if (double.IsNaN(left)) left = 0;
            if (double.IsNaN(top)) top = 0;

            double panelW = _buildPanel.ActualWidth > 0 ? _buildPanel.ActualWidth : _buildPanel.Width;
            double panelH = _buildPanel.ActualHeight > 0 ? _buildPanel.ActualHeight : _buildPanel.Height;
            double maxX = Math.Max(0, GetCanvasWidth() - panelW - 8);
            double maxY = Math.Max(0, GetCanvasHeight() - panelH - 8);

            Canvas.SetLeft(_buildPanel, Math.Max(0, Math.Min(left, maxX)));
            Canvas.SetTop(_buildPanel, Math.Max(0, Math.Min(top, maxY)));
        }

        private void OnPanelMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_buildPanel == null || _canvas == null) return;

            _dragging = true;
            _dragStart = e.GetPosition(_canvas);

            // Canvas.GetLeft/GetTop возвращают NaN, если позиция ещё не задана
            double curLeft = Canvas.GetLeft(_buildPanel);
            double curTop = Canvas.GetTop(_buildPanel);
            _panelLeft = double.IsNaN(curLeft) ? 0 : curLeft;
            _panelTop = double.IsNaN(curTop) ? 0 : curTop;

            _buildPanel.CaptureMouse();
            e.Handled = true;
        }

        private void OnPanelMouseMove(object sender, MouseEventArgs e)
        {
            if (!_dragging || _buildPanel == null || _canvas == null) return;

            var pos = e.GetPosition(_canvas);

            // ActualWidth/Height вместо Width: у auto-элементов Width == NaN
            double panelW = _buildPanel.ActualWidth > 0 ? _buildPanel.ActualWidth : _buildPanel.Width;
            double panelH = _buildPanel.ActualHeight > 0 ? _buildPanel.ActualHeight : _buildPanel.Height;
            double maxX = Math.Max(0, GetCanvasWidth() - panelW - 8);
            double maxY = Math.Max(0, GetCanvasHeight() - panelH - 8);

            double newLeft = Math.Max(0, Math.Min(_panelLeft + (pos.X - _dragStart.X), maxX));
            double newTop = Math.Max(0, Math.Min(_panelTop + (pos.Y - _dragStart.Y), maxY));

            Canvas.SetLeft(_buildPanel, newLeft);
            Canvas.SetTop(_buildPanel, newTop);
            e.Handled = true;
        }

        private void OnPanelMouseUp(object sender, MouseButtonEventArgs e)
        {
            // Без этой проверки «лишний» MouseUp (после потери захвата) сбросит чужой драг
            if (!_dragging) return;

            _dragging = false;
            _buildPanel?.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void OnPanelLostMouseCapture(object sender, MouseEventArgs e)
        {
            // Alt-Tab, скрытие оверлея и т.п. — иначе панель «поедет» без зажатой кнопки
            _dragging = false;
        }

        public void SetVisible(bool visible)
        {
            if (_canvas == null || _buildPanel == null) return;

            _canvas.Dispatcher.Invoke(() =>
            {
                _buildPanel.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
                if (visible) ClampPanelToBounds();
            });
        }

        public void UpdateBuildStatus(ArchetypeSummary summary)
        {
            if (_canvas == null || _buildPanel == null) return;

            _canvas.Dispatcher.Invoke(() =>
            {
                ClampPanelToBounds();
                _buildTitle.Text = summary.Name;
                _buildSubtitle.Text = summary.Subtitle;
                _progressBar.Value = Math.Max(0, Math.Min(100, summary.SynergyPower));
                _powerMeter.Text = $"⚡ Синергия: {summary.SynergyPower}%";
                _powerMeter.Foreground = summary.SynergyPower >= 50 ? Brushes.Gold : Brushes.LimeGreen;
            });
        }

        public void UpdateAdvice(ActionAdvice advice)
        {
            if (_canvas == null || _adviceText == null || advice == null) return;

            _canvas.Dispatcher.Invoke(() =>
            {
                _adviceText.Text = $"👉 {advice.Action}: {advice.Reason}";
            });
        }

        public void UpdateTavernHighlights(List<ScoredSlot> items)
        {
            if (_canvas == null) return;

            _canvas.Dispatcher.Invoke(() =>
            {
                ClearHighlights();

                if (items == null || items.Count == 0) return;

                // Подсвечиваем карты со скором >= 2.0
                var targets = items.Where(x => x.Score >= 2.0).ToList();
                if (targets.Count == 0) return;

                double screenW = GetCanvasWidth();
                double screenH = GetCanvasHeight();

                // Геометрия слотов таверны (до 7 слотов, центрирование)
                double cardW = screenW * 0.078;
                double cardH = screenH * 0.126;
                double stepX = screenW * 0.0935;
                double tavernY = screenH * 0.290;
                double centerX = screenW / 2.0;

                foreach (var item in targets)
                {
                    double offsetFromCenter = (item.SlotIndex - ((item.TotalSlots - 1) / 2.0)) * stepX;
                    double posX = centerX + offsetFromCenter - (cardW / 2.0);
                    double posY = tavernY - (cardH / 2.0);

                    bool isHighPriority = item.Score >= 5.0;
                    Color strokeColor = isHighPriority
                        ? Color.FromRgb(0xD4, 0xAF, 0x37) // золото
                        : Color.FromRgb(0x4A, 0xDE, 0x80); // зелёный

                    // 1) Тонкий контур строго по границе карты — арт карты остаётся видимым
                    Rectangle contour = new Rectangle
                    {
                        Width = cardW,
                        Height = cardH,
                        Stroke = new SolidColorBrush(strokeColor),
                        StrokeThickness = 2.5,
                        RadiusX = 14,
                        RadiusY = 14,
                        Fill = new SolidColorBrush(Color.FromArgb(24, 255, 255, 255)),
                        IsHitTestVisible = false
                    };

                    Canvas.SetLeft(contour, posX);
                    Canvas.SetTop(contour, posY);
                    _canvas.Children.Add(contour);
                    _dynamicElements.Add(contour);

                    // 2) Мягкая линия-свечение под картой (ничего не перекрывает)
                    Rectangle glow = new Rectangle
                    {
                        Width = cardW * 0.8,
                        Height = 4,
                        RadiusX = 2,
                        RadiusY = 2,
                        Fill = new SolidColorBrush(Color.FromArgb(210, strokeColor.R, strokeColor.G, strokeColor.B)),
                        IsHitTestVisible = false
                    };

                    Canvas.SetLeft(glow, posX + cardW * 0.1);
                    Canvas.SetTop(glow, posY + cardH + 6);
                    _canvas.Children.Add(glow);
                    _dynamicElements.Add(glow);

                    // 3) Компактный бейдж оценки под картой
                    Border badge = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(215, 10, 12, 16)),
                        BorderBrush = new SolidColorBrush(strokeColor),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(6),
                        Padding = new Thickness(6, 2, 6, 2),
                        IsHitTestVisible = false
                    };

                    var badgeText = new TextBlock
                    {
                        Text = $"★ {item.Score:0.#}",
                        FontSize = 10.5,
                        FontWeight = FontWeights.ExtraBold,
                        Foreground = new SolidColorBrush(strokeColor)
                    };
                    badge.Child = badgeText;

                    Canvas.SetLeft(badge, posX + (cardW / 2.0) - 21);
                    Canvas.SetTop(badge, posY + cardH + 13);
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
                    // Снимаем регистрацию интерактивности, чтобы HDT не держал ссылку
                    OverlayExtensions.SetIsOverlayHitTestVisible(_buildPanel, false);
                    _canvas.Children.Remove(_buildPanel);
                    _buildPanel = null;
                }
            });
        }
    }
}

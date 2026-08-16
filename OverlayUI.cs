using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Utility.Extensions;
using Newtonsoft.Json;

namespace BGSnowballEngine
{
    /// <summary>Единый снимок для отрисовки панели (одна перерисовка за апдейт).</summary>
    public class PanelUpdate
    {
        public ArchetypeSummary Summary { get; set; }
        public GameStateSnapshot State { get; set; }
        public ActionAdvice Advice { get; set; }
        public TavernOffer BestOffer { get; set; }
        public string GoalText { get; set; } = "";
    }

    public class OverlayUI
    {
        private Canvas _canvas;
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
        private TextBlock _economyText;
        private TextBlock _bestCardText;
        private TextBlock _goalText;
        private TextBlock _adviceText;

        private readonly string _configPath;

        private class PanelPosition
        {
            public double Left { get; set; }
            public double Top { get; set; }
        }

        public OverlayUI()
        {
            // Конфиг позиции панели — как у HDT: позиция переживает перезапуск
            _configPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "BGSnowballEngine", "config.json");

            _canvas = Core.OverlayCanvas;
            if (_canvas != null)
            {
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

        private PanelPosition LoadPosition()
        {
            try
            {
                if (File.Exists(_configPath))
                {
                    return JsonConvert.DeserializeObject<PanelPosition>(File.ReadAllText(_configPath));
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
            return null;
        }

        private void SavePosition()
        {
            try
            {
                if (_buildPanel == null) return;

                double left = Canvas.GetLeft(_buildPanel);
                double top = Canvas.GetTop(_buildPanel);
                if (double.IsNaN(left) || double.IsNaN(top)) return;

                string dir = Path.GetDirectoryName(_configPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(_configPath, JsonConvert.SerializeObject(new PanelPosition { Left = left, Top = top }));
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        }

        private void InitRightSidePanel()
        {
            if (_canvas == null) return;

            _canvas.Dispatcher.Invoke(() =>
            {
                if (_buildPanel != null) return;

                _buildPanel = new Border
                {
                    Width = 232,
                    Background = new SolidColorBrush(Color.FromArgb(228, 12, 16, 22)),
                    BorderBrush = new SolidColorBrush(Color.FromArgb(220, 212, 175, 55)),
                    BorderThickness = new Thickness(1.5),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(10, 8, 10, 8),
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

                // Интерактивность панели: HDT снимает клик-сквозной режим при наведении
                OverlayExtensions.SetIsOverlayHitTestVisible(_buildPanel, true);

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
                    Text = "🎯 СОВЕТНИК",
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

                _economyText = new TextBlock
                {
                    Text = "💰 0 · Тир 0 · ❤️ 0",
                    FontSize = 11,
                    Foreground = Brushes.LightSteelBlue,
                    Margin = new Thickness(0, 2, 0, 2)
                };

                _bestCardText = new TextBlock
                {
                    Text = "🛒 Лучшая карта: —",
                    FontSize = 11,
                    Foreground = Brushes.WhiteSmoke,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 0, 2)
                };

                _goalText = new TextBlock
                {
                    Text = "Цель: —",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(0x9A, 0xD8, 0xFF)),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    Margin = new Thickness(0, 0, 0, 4)
                };

                _adviceText = new TextBlock
                {
                    Text = "👉 Ждать: конец хода",
                    FontSize = 11.5,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromArgb(240, 212, 175, 55)),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                };

                stack.Children.Add(header);
                stack.Children.Add(_buildTitle);
                stack.Children.Add(_buildSubtitle);
                stack.Children.Add(_progressBar);
                stack.Children.Add(_economyText);
                stack.Children.Add(_bestCardText);
                stack.Children.Add(_goalText);
                stack.Children.Add(_adviceText);

                _buildPanel.Child = stack;

                // Восстанавливаем сохранённую позицию (как в HDT), иначе — правый край.
                // (0,0) — валидная позиция (левый верхний угол): проверяем только NaN.
                var saved = LoadPosition();
                if (saved != null && !double.IsNaN(saved.Left) && !double.IsNaN(saved.Top))
                {
                    Canvas.SetLeft(_buildPanel, saved.Left);
                    Canvas.SetTop(_buildPanel, saved.Top);
                }
                else
                {
                    UpdatePanelPosition();
                }

                _canvas.Children.Add(_buildPanel);
            });
        }

        private void UpdatePanelPosition()
        {
            double screenW = GetCanvasWidth();
            double screenH = GetCanvasHeight();

            Canvas.SetLeft(_buildPanel, screenW - 257);
            Canvas.SetTop(_buildPanel, screenH * 0.22);
        }

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
            if (!_dragging) return;

            _dragging = false;
            _buildPanel?.ReleaseMouseCapture();
            SavePosition();
            e.Handled = true;
        }

        private void OnPanelLostMouseCapture(object sender, MouseEventArgs e)
        {
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

        /// <summary>Единая перерисовка панели: сборка, экономика, лучшая карта, цель, совет.</summary>
        public void UpdatePanel(PanelUpdate update)
        {
            if (_canvas == null || _buildPanel == null) return;

            _canvas.Dispatcher.Invoke(() =>
            {
                ClampPanelToBounds();

                if (update.Summary != null)
                {
                    _buildTitle.Text = update.Summary.Name;
                    _buildSubtitle.Text = update.Summary.Subtitle;
                    _progressBar.Value = Math.Max(0, Math.Min(100, update.Summary.SynergyPower));
                }

                if (update.State != null)
                {
                    string turn = update.State.Turn > 0 ? $" · Ход {update.State.Turn}" : "";
                    _economyText.Text = $"💰 {update.State.Gold} 🪙 · Тир {update.State.TavernTier} · ❤️ {update.State.Health}{turn}";
                }

                if (update.BestOffer != null && update.BestOffer.Card != null)
                {
                    string triple = update.BestOffer.IsTriplet ? " (триплет!)" : "";
                    _bestCardText.Text = $"🛒 {update.BestOffer.Card.Name} ★{update.BestOffer.Score:0.#}{triple}";
                }
                else
                {
                    _bestCardText.Text = "🛒 Лучшая карта: —";
                }

                _goalText.Text = string.IsNullOrEmpty(update.GoalText) ? "Цель: —" : $"Цель: {update.GoalText}";

                if (update.Advice != null)
                {
                    _adviceText.Text = $"👉 {update.Advice.Action}: {update.Advice.Reason}";
                }
            });
        }

        public void ClearOverlay()
        {
            if (_canvas == null) return;

            try
            {
                _canvas.Dispatcher.Invoke(() =>
                {
                    if (_buildPanel != null)
                    {
                        OverlayExtensions.SetIsOverlayHitTestVisible(_buildPanel, false);
                        _canvas.Children.Remove(_buildPanel);
                        _buildPanel = null;
                    }
                });
            }
            catch (Exception ex)
            {
                // При выгрузке плагина во время завершения HDT dispatcher может быть закрыт
                Logger.Log(ex);
            }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Plugins;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Hearthstone;

namespace BGSnowballEngine
{
    public class PluginData : IPlugin
    {
        public string Name => "Battlegrounds Snowball Engine";
        public string Description => "Эвристический анализатор для Полей Сражений.";
        public string ButtonText => "Настройки";
        public string Author => "AI & User";
        public Version Version => new Version(1, 0, 0);
        
        public MenuItem MenuItem => null;

        private EngineCore _engine;
        private OverlayUI _overlay;
        private static string _logFile;

        public static void WriteLog(string message)
        {
            try
            {
                if (string.IsNullOrEmpty(_logFile))
                {
                    string dir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                    _logFile = Path.Combine(dir, "debug.log");
                }
                File.AppendAllText(_logFile, $"[{DateTime.Now:HH:mm:ss}] {message}\r\n");
            }
            catch { }
        }

        public void OnLoad()
        {
            WriteLog("Плагин загружается...");
            _engine = new EngineCore();
            _engine.Initialize();

            _overlay = new OverlayUI();
            
            GameEvents.OnTurnStart.Add(player => 
            {
                WriteLog($"Событие: Начат новый ход ({player})");
                AnalyzeAndDraw();
            });

            WriteLog("Плагин успешно загружен и подписан на события.");
        }

        public void OnUnload()
        {
            WriteLog("Плагин выгружен.");
            _overlay?.ClearOverlay();
        }

        public void OnButtonPress() { }

        public void OnUpdate()
        {
            AnalyzeAndDraw();
        }

        private void AnalyzeAndDraw()
        {
            try
            {
                if (Core.Game == null || Core.Game.Player == null) return;

                var playerEntities = Core.Game.Player.Board;
                var tavernEntities = Core.Game.Opponent != null ? Core.Game.Opponent.Board : null;

                if (playerEntities != null && tavernEntities != null && _engine != null && _overlay != null)
                {
                    var boardCards = playerEntities.Select(e => e.Card).Where(c => c != null).ToList();
                    var tavernCards = tavernEntities.Select(e => e.Card).Where(c => c != null).ToList();

                    if (tavernCards.Count > 0)
                    {
                        var scored = _engine.EvaluateTavern(tavernCards, boardCards);
                        _overlay.UpdateHighlights(scored);
                    }
                }
            }
            catch (Exception ex)
            {
                WriteLog($"Ошибка в AnalyzeAndDraw: {ex.Message}");
            }
        }
    }
}

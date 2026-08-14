using System;
using System.Linq;
using System.Windows.Controls;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Plugins;
using Hearthstone_Deck_Tracker.Enums;
using Hearthstone_Deck_Tracker.Hearthstone;

namespace BGSnowballEngine
{
    public class PluginData : IPlugin
    {
        public string Name { get { return "Battlegrounds Snowball Engine"; } }
        public string Description { get { return "Эвристический анализатор для Полей Сражений."; } }
        public string ButtonText { get { return "Настройки"; } }
        public string Author { get { return "AI & User"; } }
        public Version Version { get { return new Version(1, 0, 0); } }
        
        public MenuItem MenuItem { get { return null; } }

        private EngineCore _engine;
        private OverlayUI _overlay;

        public void OnLoad()
        {
            _engine = new EngineCore();
            _engine.Initialize();

            _overlay = new OverlayUI();
            
            // Передаем правильный делегат с аргументом ActivePlayer
            GameEvents.OnTurnStart.Add(player => AnalyzeAndDraw());
        }

        public void OnUnload()
        {
            if (_overlay != null)
            {
                _overlay.ClearOverlay();
            }
        }

        public void OnButtonPress() { }
        public void OnUpdate() { }

        private void AnalyzeAndDraw()
        {
            if (Core.Game == null || Core.Game.Player == null) return;

            var playerEntities = Core.Game.Player.Board;
            var tavernEntities = (Core.Game.CurrentGameStats != null && Core.Game.CurrentGameStats.GameMode == GameMode.Battlegrounds && Core.Game.Opponent != null) 
                                 ? Core.Game.Opponent.Board 
                                 : null; 

            if (playerEntities != null && tavernEntities != null && _engine != null && _overlay != null)
            {
                // Конвертируем Entity в объекты Card
                var boardCards = playerEntities.Select(e => e.Card).Where(c => c != null).ToList();
                var tavernCards = tavernEntities.Select(e => e.Card).Where(c => c != null).ToList();

                var scored = _engine.EvaluateTavern(tavernCards, boardCards);
                _overlay.UpdateHighlights(scored);
            }
        }
    }
}

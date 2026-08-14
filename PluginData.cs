using System;
using System.Windows.Controls;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Plugins;
using Hearthstone_Deck_Tracker.Enums;

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
            GameEvents.OnTurnStart.Add(AnalyzeAndDraw);
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

            var board = Core.Game.Player.Board;
            var tavern = (Core.Game.CurrentGameStats != null && Core.Game.CurrentGameStats.GameMode == GameMode.Battlegrounds && Core.Game.Opponent != null) 
                         ? Core.Game.Opponent.Board 
                         : null; 

            if (board != null && tavern != null && _engine != null && _overlay != null)
            {
                var scored = _engine.EvaluateTavern(tavern, board);
                _overlay.UpdateHighlights(scored);
            }
        }
    }
}

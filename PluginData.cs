using System;
using System.Windows.Controls;
using HearthstoneDeckTracker.API;
using HearthstoneDeckTracker.Plugins;

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
            var board = Core.Game.Player.Board;
            var tavern = (Core.Game.CurrentGameStats != null && Core.Game.CurrentGameStats.GameMode == HearthstoneDeckTracker.Enums.GameMode.Battlegrounds) 
                         ? Core.Game.Opponent.Board 
                         : null; 

            if (board != null && tavern != null)
            {
                var scored = _engine.EvaluateTavern(tavern, board);
                _overlay.UpdateHighlights(scored);
            }
        }
    }
}

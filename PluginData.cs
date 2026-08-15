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
        public string Name => "Battlegrounds Snowball Engine";
        public string Description => "Эвристический анализатор для Полей Сражений.";
        public string ButtonText => "Настройки";
        public string Author => "AI & User";
        public Version Version => new Version(1, 0, 0);
        
        public MenuItem MenuItem => null;

        private EngineCore _engine;
        private OverlayUI _overlay;

        public void OnLoad()
        {
            _engine = new EngineCore();
            _engine.Initialize();

            _overlay = new OverlayUI();
            
            // Подписываемся и на начало хода, и на обновление стола
            GameEvents.OnTurnStart.Add(player => AnalyzeAndDraw());
            GameEvents.OnGameUpdate.Add(AnalyzeAndDraw);
        }

        public void OnUnload()
        {
            _overlay?.ClearOverlay();
        }

        public void OnButtonPress() { }
        public void OnUpdate() { }

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

                    var scored = _engine.EvaluateTavern(tavernCards, boardCards);
                    _overlay.UpdateHighlights(scored);
                }
            }
            catch (Exception)
            {
                // Защита от вылетов
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Controls;
using HearthDb.Enums;
using Hearthstone_Deck_Tracker.API;
using Hearthstone_Deck_Tracker.Plugins;
using Hearthstone_Deck_Tracker.Hearthstone;

namespace BGSnowballEngine
{
    public class PluginData : IPlugin
    {
        public string Name => "Battlegrounds Snowball Engine";
        public string Description => "Эвристический анализатор для Полей Сражений.";
        public string ButtonText => "Настройки";
        public string Author => "AI & User";
        public Version Version => new Version(1, 1, 0);

        public MenuItem MenuItem => null;

        private const int UpdateIntervalMs = 300;
        private DateTime _lastUpdateUtc = DateTime.MinValue;
        private string _lastSignature = string.Empty;

        private EngineCore _engine;
        private OverlayUI _overlay;

        public void OnLoad()
        {
            _engine = new EngineCore();
            _engine.Initialize();

            _overlay = new OverlayUI();

            GameEvents.OnTurnStart.Add(player => AnalyzeAndDraw());
        }

        public void OnUnload()
        {
            _overlay?.ClearOverlay();
        }

        public void OnButtonPress() { }

        public void OnUpdate()
        {
            // Троттлинг: не дёргаем анализ и перерисовку каждый тик HDT
            if ((DateTime.UtcNow - _lastUpdateUtc).TotalMilliseconds < UpdateIntervalMs) return;
            _lastUpdateUtc = DateTime.UtcNow;

            AnalyzeAndDraw();
        }

        private void AnalyzeAndDraw()
        {
            try
            {
                if (Core.Game == null || Core.Game.Player == null) return;

                // Проверка наличия матча
                if (!Core.Game.IsBattlegroundsMatch && Core.Game.Opponent?.Name != "Бармен Боб" && Core.Game.Opponent?.Name != "Bartender Bob")
                {
                    _lastSignature = string.Empty;
                    _overlay?.ClearHighlights();
                    return;
                }

                var playerEntities = Core.Game.Player.Board?.Where(e => e != null && e.Card != null && e.IsMinion).ToList();
                var boardCards = playerEntities != null ? playerEntities.Select(e => e.Card).ToList() : new List<Card>();

                // Существа таверны, отсортированные строго слева направо
                var tavernEntities = Core.Game.Opponent?.Board?
                    .Where(e => e != null && e.Card != null && e.IsMinion && e.IsInPlay)
                    .OrderBy(e => e.GetTag(GameTag.ZONE_POSITION))
                    .ToList();

                // Пересчёт только при реальном изменении состояния (доска/таверна)
                string signature = BuildStateSignature(
                    boardCards.Select(c => c.Id),
                    tavernEntities != null ? tavernEntities.Select(e => e.Card.Id) : Enumerable.Empty<string>());
                if (signature == _lastSignature) return;
                _lastSignature = signature;

                var scoredSlots = new List<ScoredSlot>();
                bool tavernHasTriplet = false;

                // 1. Всегда обновляем плашку сборки
                if (_engine != null && _overlay != null)
                {
                    var buildSummary = _engine.AnalyzeBuild(boardCards);
                    _overlay.UpdateBuildStatus(buildSummary);
                }

                // 2. Подсветка карт в таверне
                if (tavernEntities != null && tavernEntities.Count > 0 && _engine != null && _overlay != null)
                {
                    int totalCount = tavernEntities.Count;

                    for (int i = 0; i < totalCount; i++)
                    {
                        var entity = tavernEntities[i];
                        var singleList = new List<Card> { entity.Card };

                        var scoreDict = _engine.EvaluateTavern(singleList, boardCards);
                        double score = scoreDict.ContainsKey(entity.Card) ? scoreDict[entity.Card] : 1.0;

                        // Золотой триплет или точное совпадение
                        if (boardCards.Count(c => c.Id == entity.Card.Id) == 2)
                        {
                            score += 10.0;
                            tavernHasTriplet = true;
                        }

                        scoredSlots.Add(new ScoredSlot
                        {
                            SlotIndex = i,
                            TotalSlots = totalCount,
                            Score = score
                        });
                    }

                    _overlay.UpdateTavernHighlights(scoredSlots);
                }
                else
                {
                    _overlay?.ClearHighlights();
                }

                // 3. Подсказка действия (тир / золото / здоровье / таверна)
                if (_engine != null && _overlay != null)
                {
                    var state = CaptureGameState();
                    state.BoardSize = boardCards.Count;

                    double bestScore = scoredSlots.Count > 0 ? scoredSlots.Max(s => s.Score) : 1.0;
                    var advice = _engine.Advise(state, boardCards, bestScore, tavernHasTriplet);
                    _overlay.UpdateAdvice(advice);
                }
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
            }
        }

        private GameStateSnapshot CaptureGameState()
        {
            try
            {
                int tier = Core.Game.Player.GetTag(GameTag.PLAYER_TECH_LEVEL);
                int gold = Core.Game.Player.Hero?.GetTag(GameTag.RESOURCES) ?? 0;
                int health = Core.Game.Player.Health;

                return new GameStateSnapshot
                {
                    TavernTier = tier,
                    Gold = gold,
                    Health = health
                };
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                return new GameStateSnapshot();
            }
        }

        private static string BuildStateSignature(IEnumerable<string> boardIds, IEnumerable<string> tavernIds)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var id in boardIds.OrderBy(x => x)) sb.Append(id).Append(',');
            sb.Append('|');
            foreach (var id in tavernIds) sb.Append(id).Append(',');
            return sb.ToString();
        }
    }
}

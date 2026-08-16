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
        public string Description => "Советник для Полей Сражений: сборка, экономика, покупка vs апгрейд таверны.";
        public string ButtonText => "Настройки";
        public string Author => "AI & User";
        public Version Version => new Version(1, 3, 0);

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
                    _overlay?.SetVisible(false);
                    return;
                }

                var playerEntities = Core.Game.Player.Board?.Where(e => e != null && e.Card != null && e.IsMinion).ToList();
                var boardCards = playerEntities != null ? playerEntities.Select(e => e.Card).ToList() : new List<Card>();

                // Существа таверны, отсортированные строго слева направо
                var tavernEntities = Core.Game.Opponent?.Board?
                    .Where(e => e != null && e.Card != null && e.IsMinion && e.IsInPlay)
                    .OrderBy(e => e.GetTag(GameTag.ZONE_POSITION))
                    .ToList();

                var state = CaptureGameState();
                state.BoardSize = boardCards.Count;

                // Пересчёт только при реальном изменении состояния
                // (доска / таверна / золото / тир / здоровье / ход)
                string signature = BuildStateSignature(
                    boardCards.Select(c => c.Id),
                    tavernEntities != null ? tavernEntities.Select(e => e.Card.Id) : Enumerable.Empty<string>(),
                    state);
                if (signature == _lastSignature) return;
                _lastSignature = signature;

                if (_engine == null || _overlay == null) return;

                // Панель советника показываем только в партии (тир > 0)
                _overlay.SetVisible(state.TavernTier > 0);

                // 1. Сборка и направление
                var buildSummary = _engine.AnalyzeBuild(boardCards);

                // 2. Оценка предложений таверны: лучшая карта, роли, триплеты
                var offers = new List<TavernOffer>();
                if (tavernEntities != null && tavernEntities.Count > 0)
                {
                    offers = _engine.EvaluateTavernOffers(tavernEntities.Select(e => e.Card), boardCards);
                }

                // 3. Совет: покупка vs апгрейд таверны по ситуации
                var advice = _engine.Advise(new AdviceContext
                {
                    State = state,
                    Board = boardCards,
                    Tavern = offers
                });

                // 4. Единая перерисовка панели
                _overlay.UpdatePanel(new PanelUpdate
                {
                    Summary = buildSummary,
                    State = state,
                    Advice = advice,
                    BestOffer = offers.FirstOrDefault(),
                    GoalText = _engine.GetNextGoal(boardCards)
                });
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
                // API HDT (проверено по исходникам): тир/золото лежат на PlayerEntity и Hero,
                // здоровье героя = HEALTH + ARMOR - DAMAGE (в BG броня поглощает урон первой).
                // Player.GetTag / Player.Health НЕ существуют.
                var playerEntity = Core.Game?.PlayerEntity;
                var hero = Core.Game?.Player?.Hero;

                int tier = Math.Max(playerEntity?.GetTag(GameTag.PLAYER_TECH_LEVEL) ?? 0,
                                    hero?.GetTag(GameTag.PLAYER_TECH_LEVEL) ?? 0);
                int gold = Math.Max(playerEntity?.GetTag(GameTag.RESOURCES) ?? 0,
                                    hero?.GetTag(GameTag.RESOURCES) ?? 0);
                int health = Math.Max(0, (hero?.GetTag(GameTag.HEALTH) ?? 0)
                                         + (hero?.GetTag(GameTag.ARMOR) ?? 0)
                                         - (hero?.GetTag(GameTag.DAMAGE) ?? 0));

                int turn = 0;
                try
                {
                    turn = Core.Game?.GetTurnNumber() ?? 0;
                }
                catch
                {
                    // В редких состояниях (меню/переходы) номер хода может быть недоступен — оставляем 0
                }

                return new GameStateSnapshot
                {
                    TavernTier = tier,
                    Gold = gold,
                    Health = health,
                    Turn = turn
                };
            }
            catch (Exception ex)
            {
                Logger.Log(ex);
                return new GameStateSnapshot();
            }
        }

        private static string BuildStateSignature(IEnumerable<string> boardIds, IEnumerable<string> tavernIds, GameStateSnapshot state)
        {
            var sb = new System.Text.StringBuilder();
            foreach (var id in boardIds.OrderBy(x => x)) sb.Append(id).Append(',');
            sb.Append('|');
            foreach (var id in tavernIds) sb.Append(id).Append(',');
            sb.Append('|').Append(state.TavernTier).Append(',');
            sb.Append(state.Gold).Append(',');
            sb.Append(state.Health).Append(',');
            sb.Append(state.Turn);
            return sb.ToString();
        }
    }
}

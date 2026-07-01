using BetEngine;
using BetEngine.Misc;
using BetEngine.Types;

namespace BetEngineTests
{
    [TestClass]
    public class UnitTests
    {
        private BettingEngine _engine = null!;

        [TestInitialize]
        public void Setup()
        {
            _engine = new BettingEngine();
        }

        [TestMethod]
        public void WinningSingleLeg_ReturnsStakeTimesOdds()
        {
            var bet = new Bet(100, CurrencySymbol.SEK,
                              new List<BetLeg>
                              {
                                  new(BetEvent.EnglandVsUsa, BetSelection.Draw, 3.0f)
                              });

            _engine.PlaceBet(bet);
            _engine.SettleBet(bet);

            Assert.AreEqual(300.0, _engine.GetSettlementsReturnSum(), 0.001);
            Assert.AreEqual(SettlementOutcome.Won, bet.Settle().Outcome);
        }

        [TestMethod]
        public void LosingSingleLeg_ReturnsZero()
        {
            var bet = new Bet(100, CurrencySymbol.SEK,
                              new List<BetLeg>
                              {
                                  new(BetEvent.EnglandVsUsa, BetSelection.TeamAWin, 2.0f)
                              });

            _engine.PlaceBet(bet);
            _engine.SettleBet(bet);

            Assert.AreEqual(0.0, _engine.GetSettlementsReturnSum(), 0.001);
            Assert.AreEqual(SettlementOutcome.Lost, bet.Settle().Outcome);
        }

        [TestMethod]
        public void VoidedSingleLeg_ReturnsOriginalStake()
        {
            var bet = new Bet(100, CurrencySymbol.SEK,
                              new List<BetLeg>
                              {
                                  new(BetEvent.SwedenVsNetherlands, BetSelection.TeamAWin, 2.5f)
                              });

            _engine.PlaceBet(bet);
            _engine.SettleBet(bet);

            Assert.AreEqual(100.0, _engine.GetSettlementsReturnSum(), 0.001);
            Assert.AreEqual(SettlementOutcome.Void, bet.Settle().Outcome);
        }

        [TestMethod]
        public void WinningAccumulator_MultipliesOdds()
        {
            var bet = new Bet(100, CurrencySymbol.SEK,
                              new List<BetLeg>
                              {
                                  new(BetEvent.EnglandVsUsa, BetSelection.Draw, 3.0f),
                                  new(BetEvent.SwedenVsJapan, BetSelection.TeamAWin, 2.5f)
                              });

            _engine.PlaceBet(bet);
            _engine.SettleBet(bet);

            Assert.AreEqual(750.0, _engine.GetSettlementsReturnSum(), 0.001);
        }

        [TestMethod]
        public void Accumulator_WithLosingLeg_ReturnsZero()
        {
            var bet = new Bet(100, CurrencySymbol.SEK,
                              new List<BetLeg>
                              {
                                  new(BetEvent.EnglandVsUsa, BetSelection.Draw, 3.0f),
                                  new(BetEvent.SwedenVsJapan, BetSelection.TeamBWin, 2.5f)
                              });

            _engine.PlaceBet(bet);
            _engine.SettleBet(bet);

            Assert.AreEqual(0.0, _engine.GetSettlementsReturnSum(), 0.001);
        }

        [TestMethod]
        public void Accumulator_WithVoidedLeg_IgnoresVoidedOdds()
        {
            var bet = new Bet(100, CurrencySymbol.SEK,
                              new List<BetLeg>
                              {
                                  new(BetEvent.EnglandVsUsa, BetSelection.Draw, 3.0f),
                                  new(BetEvent.SwedenVsNetherlands, BetSelection.TeamAWin, 2.0f)
                              });

            _engine.PlaceBet(bet);
            _engine.SettleBet(bet);

            Assert.AreEqual(300.0, _engine.GetSettlementsReturnSum(), 0.001);
        }

        [TestMethod]
        public void AllVoidedLegs_ReturnStake()
        {
            var bet = new Bet(100, CurrencySymbol.SEK,
                              new List<BetLeg>
                              {
                                  new(BetEvent.SwedenVsNetherlands, BetSelection.TeamAWin, 2.0f),
                                  new(BetEvent.SwedenVsNetherlands, BetSelection.TeamBWin, 5.0f)
                              });

            _engine.PlaceBet(bet);
            _engine.SettleBet(bet);

            Assert.AreEqual(100.0, _engine.GetSettlementsReturnSum(), 0.001);
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Constructor_ZeroStake_Throws()
        {
            new Bet(0, CurrencySymbol.SEK,
                    new List<BetLeg>
                    {
                        new(BetEvent.EnglandVsUsa, BetSelection.Draw, 2.0f)
                    });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Constructor_NegativeStake_Throws()
        {
            new Bet(-100, CurrencySymbol.SEK,
                    new List<BetLeg>
                    {
                        new(BetEvent.EnglandVsUsa, BetSelection.Draw, 2.0f)
                    });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Constructor_ZeroOdds_Throws()
        {
            new Bet(100, CurrencySymbol.SEK,
                    new List<BetLeg>
                    {
                        new(BetEvent.EnglandVsUsa, BetSelection.Draw, 0.0f)
                    });
        }

        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Constructor_NegativeOdds_Throws()
        {
            new Bet(100, CurrencySymbol.SEK,
                    new List<BetLeg>
                    {
                        new(BetEvent.EnglandVsUsa, BetSelection.Draw, -2.0f)
                    });
        }

        [TestMethod]
        public void SettleBet_AddsSettlementToEngine()
        {
            var bet = new Bet(50, CurrencySymbol.SEK,
                              new List<BetLeg>
                              {
                                  new(BetEvent.EnglandVsUsa, BetSelection.Draw, 3.0f)
                              });

            _engine.PlaceBet(bet);
            _engine.SettleBet(bet);

            Assert.AreEqual(150.0, _engine.GetSettlementsReturnSum(), 0.001);
        }

        [TestMethod]
        public void SettlingUnplacedBet_DoesNothing()
        {
            var bet = new Bet(100, CurrencySymbol.SEK,
                              new List<BetLeg>
                              {
                                  new(BetEvent.EnglandVsUsa, BetSelection.Draw, 3.0f)
                              });

            _engine.SettleBet(bet);

            Assert.AreEqual(0.0, _engine.GetSettlementsReturnSum(), 0.001);
        }

        [TestMethod]
        public void SettleBets_SettlesAllPlacedBets()
        {
            var bet1 = new Bet(100, CurrencySymbol.SEK,
                               new List<BetLeg>
                               {
                                   new(BetEvent.EnglandVsUsa, BetSelection.Draw, 3.0f)
                               });

            var bet2 = new Bet(100, CurrencySymbol.SEK,
                               new List<BetLeg>
                               {
                                   new(BetEvent.SwedenVsJapan, BetSelection.TeamAWin, 2.0f)
                               });

            var bet3 = new Bet(100, CurrencySymbol.SEK,
                               new List<BetLeg>
                               {
                                   new(BetEvent.EnglandVsUsa, BetSelection.TeamAWin, 2.0f)
                               });

            _engine.PlaceBet(bet1);
            _engine.PlaceBet(bet2);
            _engine.PlaceBet(bet3);

            _engine.SettleBets();

            Assert.AreEqual(500.0, _engine.GetSettlementsReturnSum(), 0.001);
        }
    }
}
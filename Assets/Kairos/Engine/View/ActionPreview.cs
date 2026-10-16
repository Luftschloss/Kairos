using System;
using System.Collections.Generic;

namespace Kairos.Engine
{
    // A payment projection, not a simulation: never resolves effects or reads hidden cards.
    public sealed class ActionPreview
    {
        public int Cost, From, Landing, NextActor, FuturePosition = -1;
        public bool Kairos, Chain, Response;
        public readonly List<int> Chimes = new List<int>();
        public readonly List<int> CrossingObjects = new List<int>();

        public static ActionPreview For(PlayerView view, Command command, CardDatabase db, int cost)
        {
            int p = command.Player, end = view.Config.TimelineEnd;
            int raw = view.Players[p].Marker + cost;
            var result = new ActionPreview
            {
                Cost = cost, From = view.Players[p].Marker, Landing = Math.Min(end, raw),
                Kairos = raw >= view.Config.BarLength && raw <= view.Config.KairosLineMax && raw % view.Config.BarLength == 0,
                Chain = view.LastDeclaredActor == p, Response = command.Kind == CommandKind.Interject
            };
            for (int line = (view.Players[p].ChimesRung + 1) * view.Config.BarLength; line <= result.Landing && line < end; line += view.Config.BarLength)
                result.Chimes.Add(line);
            foreach (var o in view.Timeline)
                if (o.Pos <= result.Landing && o.Pos > result.From && (o.Kind == ObjectKind.Omen || o.Owner == p)) result.CrossingObjects.Add(o.Id);
            // At equal markers the paying player arrived later. A pending original action
            // must still settle after a response, so do not predict the next actor there.
            result.NextActor = result.Response ? -2 : result.Landing >= end ? (view.Players[1 - p].Finished ? -1 : 1 - p)
                : view.Players[1 - p].Finished || result.Landing <= view.Players[1 - p].Marker ? p : 1 - p;
            if (command.Kind == CommandKind.Omen) result.FuturePosition = raw + command.Offset;
            else if (command.CardId != 0)
            {
                var card = GameEngine.FindCard(view.Players[p].Hand, command.CardId);
                if (card != null && db.Card(card.Key).Channel.HasValue) result.FuturePosition = raw + db.Card(card.Key).Channel.Value;
            }
            return result;
        }
    }
}

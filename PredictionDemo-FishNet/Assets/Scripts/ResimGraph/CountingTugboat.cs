// Tugboat that reports the bytes FishNet hands it to send, so ResimGraph can draw NET OUT.
// FishNet has no public outgoing data event: NetworkTrafficStatistics keeps its byte totals internal,
// and Tugboat's LiteNetLib NetManager is internal too. Transport behaviour is unchanged.
//
// Not used yet. To enable: add this component to the NetworkManager GameObject in the Gameplay scene.
// FishNet's TransportManager then picks it up instead of adding a stock Tugboat at runtime.
// See ResimGraph.md.

using System;
using FishNet.Transporting.Tugboat;

namespace PredictionDebug
{
    public class CountingTugboat : Tugboat
    {
        /// <summary>Raised for every segment passed to the socket: (asServer, bytes). Payload bytes, without LiteNetLib/UDP headers, the same basis as Transport.OnClientReceivedData.</summary>
        public event Action<bool, int> OnDataSent;

        public override void SendToServer(byte channelId, ArraySegment<byte> segment)
        {
            OnDataSent?.Invoke(false, segment.Count);
            base.SendToServer(channelId, segment);
        }

        public override void SendToClient(byte channelId, ArraySegment<byte> segment, int connectionId)
        {
            OnDataSent?.Invoke(true, segment.Count);
            base.SendToClient(channelId, segment, connectionId);
        }
    }
}

using System.Linq;
using SubnauticaMP.Shared;
using UnityEngine;

namespace SubnauticaMP
{
    // Emotes: G (or "/e wave" in chat) plays one, everyone else's game animates your diver.
    // You can't see your own body in first person, so you get a line in the message feed instead.
    internal sealed class EmoteSync
    {
        const float ResendSeconds = 4f;   // looping emotes: so people who join late see them too
        const float Cooldown = 0.35f;
        const float MoveToStop = 1.5f;    // dancing ends once you swim this far

        readonly Session _s;
        Emote _local = Emote.None;
        float _started, _nextSend, _cooldownUntil;
        Vector3 _startPos;

        public EmoteSync(Session s) { _s = s; }

        public Emote Local => _local;

        public void Reset() => _local = Emote.None;

        public void Play(Emote emote)
        {
            if (!_s.Joined) return;
            if (emote == Emote.None) { Stop(); return; }
            var info = Emotes.Get(emote);
            if (info == null || Time.unscaledTime < _cooldownUntil) return;
            var player = Game.LocalPlayer;
            if (player != null && Game.PlayerVehicle(player) != null)
            {
                _s.AddChat("Get out of the vehicle to do emotes (nobody can see you in there).");
                return;
            }
            _cooldownUntil = Time.unscaledTime + Cooldown;
            _local = emote;
            _started = Time.unscaledTime;
            _nextSend = _started + ResendSeconds;
            _startPos = PlayerPosition() ?? Vector3.zero;
            _s.Send(new EmotePacket { Emote = emote });
            _s.AddChat($"* {Plugin.PlayerName.Value} {info.Did}" + (info.Seconds <= 0f ? " (move to stop)" : ""));
        }

        public void Stop()
        {
            if (_local == Emote.None) return;
            _local = Emote.None;
            if (_s.Joined) _s.Send(new EmotePacket { Emote = Emote.None });
        }

        public void Update()
        {
            if (_local == Emote.None) return;
            var info = Emotes.Get(_local);
            float now = Time.unscaledTime;
            if (info == null) { _local = Emote.None; return; }
            if (info.Seconds > 0f)
            {
                if (now - _started >= info.Seconds) _local = Emote.None; // finished by itself everywhere
                return;
            }

            var player = Game.LocalPlayer;
            var pos = PlayerPosition();
            if (player == null || pos == null || (pos.Value - _startPos).magnitude > MoveToStop || Game.PlayerVehicle(player) != null)
            {
                Stop();
                return;
            }
            if (now >= _nextSend)
            {
                _nextSend = now + ResendSeconds;
                _s.Send(new EmotePacket { Emote = _local });
            }
        }

        public void OnEmote(EmotePacket packet)
        {
            var remote = _s.RemotePlayers.FirstOrDefault(r => r != null && r.Id == packet.Id);
            if (remote == null) return;
            if (packet.Emote == Emote.None) { remote.StopEmote(); return; }
            var info = Emotes.Get(packet.Emote);
            if (info == null) return;
            if (remote.PlayEmote(packet.Emote)) _s.AddChat($"* {remote.PlayerName} {info.Did}");
        }

        static Vector3? PlayerPosition()
        {
            var player = Game.LocalPlayer;
            return player != null ? player.transform.position : (Vector3?)null;
        }
    }
}

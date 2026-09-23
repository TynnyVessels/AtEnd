using System;
using Godot;

namespace AtEnd.App.Audio;

public sealed class GodotAudioClock
{
    private readonly AudioStreamPlayer _player;
    private readonly double _outputLatencySeconds;
    private double _lastRawPlaybackTime;
    private double _completedLoopTime;
    private double _lastReportedTime;

    public GodotAudioClock(AudioStreamPlayer player)
    {
        _player = player ?? throw new ArgumentNullException(nameof(player));
        _outputLatencySeconds = AudioServer.GetOutputLatency();
    }

    public double CurrentTimeSeconds
    {
        get
        {
            if (!_player.Playing)
            {
                return _lastReportedTime;
            }

            double rawPlaybackTime = _player.GetPlaybackPosition() + AudioServer.GetTimeSinceLastMix();
            if (rawPlaybackTime + 0.5 < _lastRawPlaybackTime)
            {
                double streamLength = _player.Stream?.GetLength() ?? 0;
                if (streamLength > 0)
                {
                    _completedLoopTime += streamLength;
                }
            }

            _lastRawPlaybackTime = rawPlaybackTime;
            double correctedTime = _completedLoopTime + rawPlaybackTime - _outputLatencySeconds;
            _lastReportedTime = Math.Max(_lastReportedTime, Math.Max(0, correctedTime));
            return _lastReportedTime;
        }
    }
}

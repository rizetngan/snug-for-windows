// Adapted from nicocart/snug-win v1.5.1 (03c4491), MIT.
// Copyright (c) 2026 AoyamaShou, zephyr4123, nicocat. See THIRD_PARTY_NOTICES.md.
using System;
using System.Collections.Generic;
using System.Text;

namespace Snug.Windows.Core;

/// <summary>
/// Pure-logic incremental tailer for <c>crossdesk.log</c>.
/// Tracks byte offsets, skips a leading partial line when starting mid-file,
/// buffers incomplete trailing bytes until newline, and resets on rotation or truncation.
/// </summary>
public sealed class CrossDeskLogFollower
{
    public const long InitialWindow = 1L << 20; // 1 MiB

    private bool _started;
    private ulong? _fileId;
    private long _offset;
    private bool _skippingPartial;
    private byte[] _pending = Array.Empty<byte>();

    /// <summary>
    /// Given the current file identity and byte length, returns the <c>(Offset, Count)</c>
    /// byte range to read on this tick, or <c>null</c> if nothing new was appended.
    /// </summary>
    public (long Offset, int Count)? NextRange(ulong? fileId, long size)
    {
        if (size < 0) size = 0;

        if (!_started)
        {
            _started = true;
            _offset = size > InitialWindow ? size - InitialWindow : 0;
            _skippingPartial = _offset > 0;
        }
        else if (fileId != _fileId || size < _offset)
        {
            _offset = 0;
            _skippingPartial = false;
            _pending = Array.Empty<byte>();
        }

        _fileId = fileId;
        if (size <= _offset)
            return null;

        long delta = size - _offset;
        int count = (int)Math.Min(delta, InitialWindow);
        return (_offset, count);
    }

    /// <summary>
    /// Feeds bytes read for <see cref="NextRange"/> and returns complete UTF-8 lines
    /// (with trailing <c>\r</c> stripped).
    /// </summary>
    public IReadOnlyList<string> Feed(ReadOnlySpan<byte> data)
    {
        _offset += data.Length;
        if (data.IsEmpty)
            return Array.Empty<string>();

        if (_skippingPartial)
        {
            int nl = data.IndexOf((byte)'\n');
            if (nl < 0)
                return Array.Empty<string>();
            data = data[(nl + 1)..];
            _skippingPartial = false;
        }

        if (data.IsEmpty && _pending.Length == 0)
            return Array.Empty<string>();

        byte[] combined;
        if (_pending.Length == 0)
        {
            combined = data.ToArray();
        }
        else
        {
            combined = new byte[_pending.Length + data.Length];
            Buffer.BlockCopy(_pending, 0, combined, 0, _pending.Length);
            data.CopyTo(combined.AsSpan(_pending.Length));
        }

        int lastNewline = Array.LastIndexOf(combined, (byte)'\n');
        if (lastNewline < 0)
        {
            if (combined.Length > InitialWindow) { _pending = Array.Empty<byte>(); _skippingPartial = true; } else _pending = combined;
            return Array.Empty<string>();
        }

        int remaining = combined.Length - (lastNewline + 1);
        _pending = remaining > 0 ? combined[(lastNewline + 1)..] : Array.Empty<byte>();

        var decoded = Encoding.UTF8.GetString(combined, 0, lastNewline);
        var rawLines = decoded.Split('\n');
        var result = new List<string>(rawLines.Length);
        foreach (var raw in rawLines)
        {
            var line = raw.EndsWith('\r') ? raw[..^1] : raw;
            if (line.Length > 0)
                result.Add(line);
        }
        return result;
    }

    public void SkipToEnd(ulong? fileId, long size)
    {
        if (size < 0) size = 0;
        _started = true;
        _fileId = fileId;
        _offset = size;
        _skippingPartial = false;
        _pending = Array.Empty<byte>();
    }

    public void Reset()
    {
        _started = false;
        _fileId = null;
        _offset = 0;
        _skippingPartial = false;
        _pending = Array.Empty<byte>();
    }
}

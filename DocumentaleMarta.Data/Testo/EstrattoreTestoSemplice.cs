using System.Text;
using DocumentaleMarta.Core.Servizi;

namespace DocumentaleMarta.Data.Testo;

/// <summary>File di solo testo (.txt, .csv).</summary>
public class EstrattoreTestoSemplice : IEstrattoreTesto
{
    /// <summary>Oltre questa dimensione si legge solo l'inizio: un file di testo di centinaia di MB non è un documento da cercare.</summary>
    private const int ByteMassimi = 5 * 1024 * 1024;

    private static readonly HashSet<string> Estensioni = [".txt", ".csv"];

    public bool Supporta(string estensione) => Estensioni.Contains(estensione);

    public async Task<string> EstraiAsync(string percorsoFile, CancellationToken cancellation)
    {
        await using var stream = new FileStream(percorsoFile, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

        var buffer = new byte[(int)Math.Min(stream.Length, ByteMassimi)];
        var letti = 0;
        while (letti < buffer.Length)
        {
            var n = await stream.ReadAsync(buffer.AsMemory(letti), cancellation);
            if (n == 0)
                break;
            letti += n;
        }

        return Decodifica(buffer.AsSpan(0, letti));
    }

    /// <summary>UTF-8 (con o senza BOM) se il contenuto lo è davvero; altrimenti Windows-1252, come i vecchi file di testo italiani.</summary>
    private static string Decodifica(ReadOnlySpan<byte> byte_)
    {
        try
        {
            var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            return utf8.GetString(byte_).TrimStart('﻿');
        }
        catch (DecoderFallbackException)
        {
            return Encoding.Latin1.GetString(byte_);
        }
    }
}

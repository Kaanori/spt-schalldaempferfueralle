// ---------------------------------------------------------------
//  Schalldaempfer fuer alle
//
//  Legt die Schalldaempfer-Werte auf JEDEN Schalldaempfer, auch auf
//  die, die andere Mods erst nachtraeglich mitbringen.
//
//  Warum ein eigener Mod:
//  BetterSuppressors arbeitet in IOnLoad und ist in der Ladereihe
//  Nummer 30 - WTT-Armory kommt aber erst als Nummer 34. Dessen 36
//  Schalldaempfer sind zu diesem Zeitpunkt noch gar nicht in der
//  Tabelle und bleiben deshalb unangetastet (gemessen 20.09.2026:
//  76 von 112 angepasst).
//
//  Einen spaeteren Einstiegspunkt gibt es im Server nicht - es gibt
//  nur IOnLoad und IOnDIConstruct. Darum zwei Durchlaeufe:
//    1. beim Serverstart (IOnLoad)
//    2. einmalig bei der ersten Client-Anfrage (IHttpListener)
//  Der zweite Durchlauf faengt alles ein, was nach dem Start
//  dazugekommen ist - unabhaengig von der Ladereihenfolge.
//  CanHandle meldet immer "nein", der Mod beantwortet also keine
//  einzige Anfrage selbst.
//
//  Die Werte kommen aus der config.json von BetterSuppressors,
//  damit es weiterhin nur EINE Datei zum Einstellen gibt. Fehlt
//  sie, wird schalldaempfer.json neben dieser DLL benutzt.
// ---------------------------------------------------------------
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Spt.Mod;
using SPTarkov.Server.Core.Models.Spt.Tables;
using SPTarkov.Server.Core.Servers.Http;
using SPTarkov.Common.Models.Logging;

using Pfad = System.IO.Path;

namespace SchalldaempferFuerAlle;

public record ModMetadata : IModMetadata
{
    public string ModGuid { get; init; } = "com.kaan.schalldaempferfueralle";
    public string Name { get; init; } = "Schalldaempfer fuer alle";
    public string Author { get; init; } = "Kaan";
    public List<string>? Contributors { get; init; } = new();
    public SemanticVersioning.Version Version { get; init; } = new(1, 0, 0);
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; } = new();
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new();
    public string? Url { get; init; } = "";
    public string License { get; init; } = "MIT";
}

/// <summary>Die sechs Werte. Schluesselnamen wie bei BetterSuppressors.</summary>
public sealed class Werte
{
    public double Ergonomics { get; set; } = 0.0;
    public double DurabilityBurn { get; set; } = 1.0;
    public double HeatFactor { get; set; } = 0.90;
    public double CoolFactor { get; set; } = 0.92;
    public double Accuracy { get; set; } = 6.0;
    public double Velocity { get; set; } = 6.0;
}

public static class Arbeit
{
    // Basisklasse "Silencer" - daran haengen alle Schalldaempfervorlagen.
    public const string SchalldaempferBasis = "550aa4cd4bdc2dd8348b456c";

    private static readonly JsonSerializerOptions Leseweise = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    public static Werte WerteLesen(out string quelle)
    {
        var eigenerOrdner = Pfad.GetDirectoryName(
            System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";

        // 1. Wahl: die Datei von BetterSuppressors - eine Datei fuer beides.
        var geteilt = Pfad.GetFullPath(
            Pfad.Combine(eigenerOrdner, "..", "BetterSuppressors", "config.json"));
        // 2. Wahl: eigene Datei daneben.
        var eigen = Pfad.Combine(eigenerOrdner, "schalldaempfer.json");

        foreach (var (pfad, name) in new[]
                 {
                     (geteilt, "config.json von BetterSuppressors"),
                     (eigen, "schalldaempfer.json")
                 })
        {
            if (!File.Exists(pfad)) continue;
            try
            {
                var w = JsonSerializer.Deserialize<Werte>(File.ReadAllText(pfad), Leseweise);
                if (w is not null)
                {
                    quelle = name;
                    return w;
                }
                quelle = name + " war leer - eingebaute Standardwerte";
                return new Werte();
            }
            catch (Exception ex)
            {
                // Bewusst laut: ein Tippfehler soll auffallen und nicht
                // stillschweigend zu anderen Werten fuehren.
                quelle = name + " unlesbar (" + ex.Message + ") - eingebaute Standardwerte";
                return new Werte();
            }
        }

        quelle = "eingebaute Standardwerte (keine Datei gefunden)";
        return new Werte();
    }

    /// <summary>Setzt die Werte auf jede Schalldaempfervorlage. Gibt zurueck,
    /// wie viele angefasst und wie viele davon vorher abwichen.</summary>
    public static (int gesamt, int geaendert) Anwenden(TemplateTable vorlagen, Werte w)
    {
        int gesamt = 0, geaendert = 0;

        foreach (var (_, vorlage) in vorlagen.Items)
        {
            if (vorlage.Parent != SchalldaempferBasis) continue;
            if (vorlage.Type != "Item") continue;

            var eig = vorlage.Properties;
            if (eig is null) continue;

            gesamt++;
            bool wichAb =
                eig.Ergonomics != w.Ergonomics ||
                eig.DurabilityBurnModificator != w.DurabilityBurn ||
                eig.HeatFactor != w.HeatFactor ||
                eig.CoolFactor != w.CoolFactor ||
                eig.Accuracy != w.Accuracy ||
                eig.Velocity != w.Velocity;

            eig.Ergonomics = w.Ergonomics;
            eig.DurabilityBurnModificator = w.DurabilityBurn;
            eig.HeatFactor = w.HeatFactor;
            eig.CoolFactor = w.CoolFactor;
            eig.Accuracy = w.Accuracy;
            eig.Velocity = w.Velocity;

            if (wichAb) geaendert++;
        }

        return (gesamt, geaendert);
    }
}

/// <summary>Erster Durchlauf: beim Serverstart.</summary>
[Injectable(InjectionType.Singleton)]
public class BeimStart(
    ISptLogger<BeimStart> logger,
    TemplateTable vorlagen) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken abbruch)
    {
        try
        {
            var w = Arbeit.WerteLesen(out var quelle);
            var (gesamt, geaendert) = Arbeit.Anwenden(vorlagen, w);

            if (gesamt == 0)
            {
                logger.Warning("[Schalldaempfer fuer alle] Keine Schalldaempfervorlagen "
                               + "gefunden - hat sich die Basisklasse geaendert?");
                return Task.CompletedTask;
            }

            logger.Success($"[Schalldaempfer fuer alle] Start: {gesamt} Schalldaempfer gesetzt "
                           + $"({geaendert} davon wichen ab). Quelle: {quelle}. "
                           + $"erg {w.Ergonomics}, burn {w.DurabilityBurn}, heat {w.HeatFactor}, "
                           + $"cool {w.CoolFactor}, acc {w.Accuracy}, vel {w.Velocity}");
        }
        catch (Exception ex)
        {
            logger.Error("[Schalldaempfer fuer alle] " + ex.Message);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Zweiter Durchlauf: einmalig bei der ersten Client-Anfrage, damit
/// auch Vorlagen erwischt werden, die nach dem Serverstart dazukamen.
/// Beantwortet selbst nie eine Anfrage.</summary>
[Injectable(InjectionType.Singleton)]
public class Nachziehen(
    ISptLogger<Nachziehen> logger,
    TemplateTable vorlagen) : IHttpListener
{
    private int _erledigt;

    public bool CanHandle(HttpContext context)
    {
        // Interlocked: der Server darf mehrere Anfragen gleichzeitig pruefen.
        if (Interlocked.Exchange(ref _erledigt, 1) == 0)
        {
            try
            {
                var w = Arbeit.WerteLesen(out _);
                var (gesamt, geaendert) = Arbeit.Anwenden(vorlagen, w);

                if (geaendert > 0)
                {
                    logger.Success($"[Schalldaempfer fuer alle] Nachgezogen: {geaendert} von "
                                   + $"{gesamt} Schalldaempfern kamen erst nach dem Serverstart "
                                   + "dazu und sind jetzt auch gesetzt.");
                }
                else
                {
                    logger.Info($"[Schalldaempfer fuer alle] Nachgezogen: nichts offen, "
                                + $"alle {gesamt} waren schon gesetzt.");
                }
            }
            catch (Exception ex)
            {
                logger.Error("[Schalldaempfer fuer alle] Nachziehen fehlgeschlagen: " + ex.Message);
            }
        }

        return false;   // immer: diese Mod beantwortet keine Anfragen
    }

    public Task HandleAsync(MongoId sessionId, HttpContext context,
                            CancellationToken abbruch = default)
        => Task.CompletedTask;
}

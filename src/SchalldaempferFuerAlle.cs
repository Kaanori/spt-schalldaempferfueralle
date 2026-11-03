// ---------------------------------------------------------------
//  Schalldaempfer fuer alle
//
//  Passt die Werte ALLER Schalldaempfer gleichzeitig an - auch die,
//  die andere Mods (WTT-Armory & Co.) erst nachtraeglich mitbringen.
//  Je Wert ein Modus: aus (Original), Wert (fest setzen),
//  Plus (Original + Zahl), Prozent (Original x (1 + Zahl/100)).
//
//  "Original" = der Wert, den der Schalldaempfer ohne diese Mod
//  haette - samt den Aenderungen anderer Mods (z. B. SVM halbiert
//  das Gewicht). Nur die festen Zahlen von BetterSuppressors werden
//  zurueckgenommen: Er laeuft mit Prioritaet 1000, also BEVOR
//  WTT-Armory & Co. (100002) ihre Schalldaempfer als Kopie von
//  Grundspiel-Schalldaempfern anlegen - ohne Ruecknahme erben diese
//  Kopien seine Zahlen, und Plus/Prozent rechneten darauf.
//
//  Zeitpunkte:
//    a. VorBS (IOnLoad, Prioritaet 999): Werte sichern.
//       NachBS (Prioritaet 1001): was BetterSuppressors an seinen
//       sechs Werten geaendert hat, sofort zuruecknehmen.
//    b. BeimStart  (IOnLoad, int.MaxValue = als Letztes): anwenden.
//    c. Nachziehen (IHttpListener, einmalig bei der ersten Anfrage):
//       alles, was nach dem Start noch dazukam.
//  Das Original wird beim ersten Anwenden festgehalten und danach
//  immer von dort gerechnet - Plus/Prozent wirken nie doppelt.
//
//  Einstellungen: config.json neben dieser DLL. Fehlt sie, werden
//  wie bisher die config.json von BetterSuppressors, dann
//  schalldaempfer.json, dann die eingebauten Standardwerte gelesen
//  (alle sechs Werte im Modus "Wert") und daraus einmalig eine
//  config.json angelegt - die Zahlen bleiben dieselben.
// ---------------------------------------------------------------
using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using SPTarkov.DI.Annotations;
using SPTarkov.Server.Core.DI;
using SPTarkov.Server.Core.Models.Common;
using SPTarkov.Server.Core.Models.Eft.Common.Tables;
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
    public SemanticVersioning.Version Version { get; init; } = new(1, 1, 0);
    public SemanticVersioning.Range SptVersion { get; init; } = new("~4.1");
    public bool HasPrepatcher { get; init; } = false;
    public List<string>? Incompatibilities { get; init; } = new();
    public Dictionary<string, SemanticVersioning.Range>? ModDependencies { get; init; } = new();
    public string? Url { get; init; } = "";
    public string License { get; init; } = "MIT";
}

public enum Modus { Aus, Wert, Plus, Prozent }

/// <summary>Beschreibung eines einstellbaren Schalldaempfer-Werts.</summary>
public sealed record Parameter(
    string Name,                    // Schluessel in der config.json
    string[] Aliase,                // weitere erlaubte Schluessel
    Func<TemplateItemProperties, double?> Lesen,
    Action<TemplateItemProperties, double> Schreiben,
    double Neutral,                 // Wert, wenn eine Vorlage ihn nicht hat
    bool Ganzzahl,                  // im Spiel ein int-Feld -> runden
    double StdMin, double StdMax,   // vorgeschlagene Grenzen
    double HartMin, double HartMax, // darueber/darunter bricht es
    string De, string En);

/// <summary>Einstellung fuer einen Wert, wie sie aus der Datei kommt.</summary>
public sealed class Regel
{
    public Modus Modus = Modus.Aus;
    public double Zahl;
    public double Min, Max;
}

public sealed class Einstellungen
{
    public Regel[] Regeln = new Regel[Arbeit.Werte.Length];
    public HashSet<string> Ausnahmen = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> NurDiese = new(StringComparer.OrdinalIgnoreCase);
    public string Quelle = "";
}

public static class Arbeit
{
    // Basisklasse "Silencer" - daran haengen alle Schalldaempfervorlagen.
    public const string SchalldaempferBasis = "550aa4cd4bdc2dd8348b456c";

    private const string Kopf = "[Schalldaempfer fuer alle] ";
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // Wirkung im Spiel belegt am Client (Assembly-CSharp, EFT.InventoryLogic):
    //  - Ergonomics, Recoil, Accuracy, Velocity: Weapon summiert die Werte
    //    aller Anbauteile (Prozentpunkte, Weapon.RecoilDelta /
    //    CenterOfImpactDelta / VelocityDelta).
    //  - DurabilityBurn, HeatFactor, CoolFactor: Weapon multipliziert die
    //    Faktoren aller Anbauteile (GetDurabilityLossOnShot,
    //    GetShotOverheat, GetCurrentOverheat). 1 = neutral.
    //  - Accuracy und Loudness sind im Client int-Felder (ModTemplate).
    //  - Loudness zeigt der Client nur an; SAIN rechnet es auf die
    //    Hoerweite der Bots drauf (BaseAudibleRange + Loudness).
    public static readonly Parameter[] Werte =
    {
        new("Ergonomics", Array.Empty<string>(),
            p => p.Ergonomics, (p, v) => p.Ergonomics = v, 0, false,
            -50, 50, double.NegativeInfinity, double.PositiveInfinity,
            "Ergonomie-Punkte der Waffe. Hoeher = besser. Original im Grundspiel -30 bis -2.",
            "Weapon ergonomics points. Higher = better. Base-game originals -30 to -2."),
        new("Recoil", Array.Empty<string>(),
            p => p.Recoil, (p, v) => p.Recoil = v, 0, false,
            -50, 50, -100, double.PositiveInfinity,
            "Rueckstoss in Prozent. Niedriger (negativer) = weniger Rueckstoss. Original im Grundspiel -15 bis -1. Nie unter -100.",
            "Recoil in percent. Lower (more negative) = less recoil. Base-game originals -15 to -1. Never below -100."),
        new("Accuracy", Array.Empty<string>(),
            p => p.Accuracy, (p, v) => p.Accuracy = v, 0, true,
            -50, 50, double.NegativeInfinity, double.PositiveInfinity,
            "Genauigkeit in Prozent. Hoeher = genauer. Ganze Zahl (wird gerundet). Original im Grundspiel -5 bis +1.",
            "Accuracy in percent. Higher = more accurate. Whole number (rounded). Base-game originals -5 to +1."),
        new("Velocity", Array.Empty<string>(),
            p => p.Velocity, (p, v) => p.Velocity = v, 0, false,
            -50, 50, -90, double.PositiveInfinity,
            "Muendungsgeschwindigkeit in Prozent. Hoeher = schnellere Kugel (mehr Reichweite, kann Unterschallmunition ueberschallschnell machen). Original im Grundspiel 0,2 bis 1,2.",
            "Muzzle velocity in percent. Higher = faster bullet (more range, can push subsonic ammo past the speed of sound). Base-game originals 0.2 to 1.2."),
        new("Loudness", Array.Empty<string>(),
            p => p.Loudness, (p, v) => p.Loudness = v, 0, true,
            -100, 100, double.NegativeInfinity, double.PositiveInfinity,
            "Lautstaerke. Niedriger = leiser. Wirkt mit SAIN auf die Hoerweite der Bots (in Metern dazugerechnet). Ganze Zahl. Original im Grundspiel -39 bis 0.",
            "Loudness. Lower = quieter. With SAIN it changes how far bots hear the shot (added in meters). Whole number. Base-game originals -39 to 0."),
        new("DurabilityBurn", new[] { "DurabilityBurnModificator" },
            p => p.DurabilityBurnModificator, (p, v) => p.DurabilityBurnModificator = v, 1, false,
            0, 10, 0, double.PositiveInfinity,
            "Faktor fuer den Verschleiss der Waffe je Schuss. 1 = neutral, niedriger = weniger Verschleiss. Original im Grundspiel 1,2 bis 2,15.",
            "Factor for weapon wear per shot. 1 = neutral, lower = less wear. Base-game originals 1.2 to 2.15."),
        new("HeatFactor", Array.Empty<string>(),
            p => p.HeatFactor, (p, v) => p.HeatFactor = v, 1, false,
            0, 10, 0, double.PositiveInfinity,
            "Faktor fuer die Erhitzung je Schuss. 1 = neutral, niedriger = Waffe wird langsamer heiss. Original im Grundspiel 1,05 bis 1,34.",
            "Factor for heat per shot. 1 = neutral, lower = weapon heats up more slowly. Base-game originals 1.05 to 1.34."),
        new("CoolFactor", Array.Empty<string>(),
            p => p.CoolFactor, (p, v) => p.CoolFactor = v, 1, false,
            0, 10, 0, double.PositiveInfinity,
            "Faktor fuer das Abkuehlen. 1 = neutral, HOEHER = kuehlt schneller ab (unter 1 = langsamer!). Original im Grundspiel 1,0 bis 1,2.",
            "Factor for cooling down. 1 = neutral, HIGHER = cools faster (below 1 = slower!). Base-game originals 1.0 to 1.2."),
        new("Weight", Array.Empty<string>(),
            p => p.Weight, (p, v) => p.Weight = v, 0, false,
            0, 5, 0, double.PositiveInfinity,
            "Gewicht in kg. Niedriger = leichter (Waffe und Inventar). Original im Grundspiel 0,13 bis 1,1.",
            "Weight in kg. Lower = lighter (weapon and inventory). Base-game originals 0.13 to 1.1."),
    };

    // Standard ohne jede Datei = bisheriges Verhalten (Werte wie BetterSuppressors).
    private static readonly Dictionary<string, double> AltStandard = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ergonomics"] = 0.0, ["DurabilityBurn"] = 1.0, ["HeatFactor"] = 0.90,
        ["CoolFactor"] = 0.92, ["Accuracy"] = 6.0, ["Velocity"] = 6.0,
    };

    private static readonly JsonDocumentOptions Leseweise = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    // BetterSuppressors' TypePriority (geprueft in BetterSuppressors.dll 1.0.0)
    public const int BsPrioritaet = 1000;

    // Die sechs Werte, die BetterSuppressors fest setzt.
    private static readonly HashSet<string> BsWerte = new(StringComparer.OrdinalIgnoreCase)
    { "Ergonomics", "DurabilityBurn", "HeatFactor", "CoolFactor", "Accuracy", "Velocity" };

    // Schnappschuss vor BetterSuppressors und die festgehaltenen
    // Originale (Template-ID -> Wert je Parameter).
    private static readonly Dictionary<string, double?[]> VorBs = new();
    private static readonly Dictionary<string, double?[]> Originale = new();

    // Was BetterSuppressors geaendert hatte und zurueckgenommen wurde.
    public static readonly int[] BsZurueck = new int[Werte.Length];
    public static int BsVorlagen;
    private static readonly object Sperre = new();
    private static Einstellungen? _einstellungen;

    // ------------------------------------------------------------
    //  Originale sichern
    // ------------------------------------------------------------

    private static IEnumerable<(string id, TemplateItemProperties eig)> Schalldaempfer(TemplateTable vorlagen)
    {
        foreach (var (id, vorlage) in vorlagen.Items)
        {
            if (vorlage.Parent != SchalldaempferBasis) continue;
            if (vorlage.Type != "Item") continue;
            if (vorlage.Properties is null) continue;
            yield return (id.ToString(), vorlage.Properties);
        }
    }

    private static double?[] Lesen(TemplateItemProperties eig)
    {
        var w = new double?[Werte.Length];
        for (int i = 0; i < Werte.Length; i++) w[i] = Werte[i].Lesen(eig);
        return w;
    }

    /// <summary>Zeitpunkt VorBS: Werte aller Schalldaempfer sichern.</summary>
    public static void VorBsSichern(TemplateTable vorlagen)
    {
        lock (Sperre)
        {
            foreach (var (id, eig) in Schalldaempfer(vorlagen))
                VorBs[id] = Lesen(eig);
        }
    }

    /// <summary>Zeitpunkt NachBS: was BetterSuppressors an seinen sechs
    /// Werten geaendert hat, auf den Stand davor zuruecksetzen.</summary>
    public static void BsZuruecknehmen(TemplateTable vorlagen)
    {
        lock (Sperre)
        {
            foreach (var (id, eig) in Schalldaempfer(vorlagen))
            {
                if (!VorBs.TryGetValue(id, out var vor)) continue;
                bool geaendert = false;
                for (int i = 0; i < Werte.Length; i++)
                {
                    var p = Werte[i];
                    if (!BsWerte.Contains(p.Name)) continue;
                    var jetzt = p.Lesen(eig);
                    if (jetzt == vor[i] || vor[i] is not { } alt) continue;
                    p.Schreiben(eig, alt);
                    BsZurueck[i]++;
                    geaendert = true;
                }
                if (geaendert) BsVorlagen++;
            }
            VorBs.Clear();
        }
    }

    /// <summary>Haelt das Original eines Schalldaempfers beim ersten
    /// Anwenden fest; spaeter wird immer von dort gerechnet.</summary>
    private static double?[] Original(string id, TemplateItemProperties eig)
    {
        if (!Originale.TryGetValue(id, out var o))
            Originale[id] = o = Lesen(eig);
        return o;
    }

    // ------------------------------------------------------------
    //  Einstellungen lesen
    // ------------------------------------------------------------

    public static bool BetterSuppressorsDa =>
        File.Exists(Pfad.Combine(EigenerOrdner, "..", "BetterSuppressors", "BetterSuppressors.dll"));

    private static string EigenerOrdner =>
        Pfad.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";

    public static Einstellungen Einstellungen(ISptLogger<BeimStart>? logger)
    {
        lock (Sperre)
        {
            return _einstellungen ??= EinstellungenLesen(logger);
        }
    }

    private static Einstellungen EinstellungenLesen(ISptLogger<BeimStart>? logger)
    {
        var neu = Pfad.Combine(EigenerOrdner, "config.json");
        bool neuKaputt = false;

        if (File.Exists(neu))
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(neu), Leseweise);
                var e = AusJson(doc.RootElement, "config.json", logger);
                if (e is not null) return e;
                logger?.Error(Kopf + "config.json ist leer oder kein JSON-Objekt.");
            }
            catch (Exception ex)
            {
                logger?.Error(Kopf + "config.json unlesbar: " + ex.Message);
            }
            // Bewusst laut und ohne die Datei zu ueberschreiben: weiter mit
            // dem alten Weg, damit wenigstens die bisherigen Werte gelten.
            neuKaputt = true;
            logger?.Error(Kopf + "config.json wird NICHT benutzt - es gelten die alten Quellen (siehe unten). Datei pruefen (Punkt als Dezimaltrennzeichen, Kommas, Anfuehrungszeichen).");
        }

        var alt = AltLesen(logger, out bool altLesbar);
        if (!neuKaputt && altLesbar)
            Anlegen(neu, alt, logger);
        return alt;
    }

    /// <summary>Bisheriger Weg: BetterSuppressors-config.json, dann
    /// schalldaempfer.json, dann eingebaute Standardwerte.</summary>
    private static Einstellungen AltLesen(ISptLogger<BeimStart>? logger, out bool lesbar)
    {
        var geteilt = Pfad.GetFullPath(Pfad.Combine(EigenerOrdner, "..", "BetterSuppressors", "config.json"));
        var eigen = Pfad.Combine(EigenerOrdner, "schalldaempfer.json");
        lesbar = true;

        foreach (var (pfad, name) in new[]
                 {
                     (geteilt, "config.json von BetterSuppressors"),
                     (eigen, "schalldaempfer.json")
                 })
        {
            if (!File.Exists(pfad)) continue;
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(pfad), Leseweise);
                if (doc.RootElement.ValueKind != JsonValueKind.Object)
                    return AltesFormat(null, name + " war leer - eingebaute Standardwerte");
                // Eine schalldaempfer.json darf auch schon das neue Format haben.
                var e = AusJson(doc.RootElement, name, logger);
                if (e is not null) return e;
            }
            catch (Exception ex)
            {
                lesbar = false;
                logger?.Error(Kopf + name + " unlesbar (" + ex.Message + ") - eingebaute Standardwerte");
                return AltesFormat(null, name + " unlesbar - eingebaute Standardwerte");
            }
        }

        return AltesFormat(null, "eingebaute Standardwerte (keine Datei gefunden)");
    }

    /// <summary>Altes, flaches Format: sechs Zahlen, alle im Modus "Wert".</summary>
    private static Einstellungen AltesFormat(JsonElement? wurzel, string quelle)
    {
        var e = new Einstellungen { Quelle = quelle };
        for (int i = 0; i < Werte.Length; i++)
        {
            var p = Werte[i];
            var r = new Regel { Min = p.StdMin, Max = p.StdMax };
            if (AltStandard.TryGetValue(p.Name, out var std))
            {
                r.Modus = Modus.Wert;
                r.Zahl = std;
                if (wurzel is { } w && Feld(w, p.Name, p.Aliase) is { } f && Zahl(f) is { } z)
                    r.Zahl = z;
                // Grenzen so weit, dass der alte Wert unveraendert durchgeht.
                r.Min = Math.Min(r.Min, r.Zahl);
                r.Max = Math.Max(r.Max, r.Zahl);
            }
            e.Regeln[i] = r;
        }
        return e;
    }

    /// <summary>Liest neues oder altes Format. null = kein Objekt.</summary>
    private static Einstellungen? AusJson(JsonElement wurzel, string name, ISptLogger<BeimStart>? logger)
    {
        if (wurzel.ValueKind != JsonValueKind.Object) return null;

        var parameter = Feld(wurzel, "Parameter", new[] { "Parameters" });
        if (parameter is not { ValueKind: JsonValueKind.Object })
            return AltesFormat(wurzel, name);   // flaches Format wie BetterSuppressors

        var e = new Einstellungen { Quelle = name };
        for (int i = 0; i < Werte.Length; i++)
        {
            var p = Werte[i];
            var r = new Regel { Min = p.StdMin, Max = p.StdMax };
            e.Regeln[i] = r;

            if (Feld(parameter.Value, p.Name, p.Aliase) is not { ValueKind: JsonValueKind.Object } block)
                continue;   // nicht aufgefuehrt = aus

            var modusText = (Feld(block, "Modus", new[] { "Mode" }) is { ValueKind: JsonValueKind.String } m
                ? m.GetString() : "aus") ?? "aus";
            switch (modusText.Trim().ToLowerInvariant())
            {
                case "aus": case "off": case "": r.Modus = Modus.Aus; break;
                case "wert": case "set": case "value": r.Modus = Modus.Wert; break;
                case "plus": case "add": r.Modus = Modus.Plus; break;
                case "prozent": case "percent": case "%": r.Modus = Modus.Prozent; break;
                default:
                    logger?.Error(Kopf + $"{name}: {p.Name} hat den unbekannten Modus \"{modusText}\" - bleibt aus. Erlaubt: aus, Wert, Plus, Prozent.");
                    r.Modus = Modus.Aus;
                    break;
            }

            var zahlFeld = Feld(block, "Zahl", new[] { "Value", "Wert" });
            if (zahlFeld is { } zf && Zahl(zf) is { } z) r.Zahl = z;
            else if (r.Modus != Modus.Aus)
            {
                logger?.Error(Kopf + $"{name}: {p.Name} hat keine gueltige \"Zahl\" - bleibt aus.");
                r.Modus = Modus.Aus;
            }

            if (Feld(block, "Min", Array.Empty<string>()) is { } mi && Zahl(mi) is { } min) r.Min = min;
            if (Feld(block, "Max", Array.Empty<string>()) is { } ma && Zahl(ma) is { } max) r.Max = max;
            if (r.Min > r.Max)
            {
                logger?.Warning(Kopf + $"{name}: {p.Name} Min ist groesser als Max - vertauscht.");
                (r.Min, r.Max) = (r.Max, r.Min);
            }
            if (r.Min < p.HartMin)
            {
                logger?.Warning(Kopf + $"{name}: {p.Name} Min {F(r.Min)} geht nicht, das Spiel braucht mindestens {F(p.HartMin)}.");
                r.Min = p.HartMin;
            }
            if (r.Max > p.HartMax) r.Max = p.HartMax;
        }

        // Unbekannte Schluessel melden (Tippfehler sollen auffallen).
        foreach (var feld in parameter.Value.EnumerateObject())
        {
            if (feld.Name.StartsWith('_')) continue;
            bool bekannt = Werte.Any(p => string.Equals(p.Name, feld.Name, StringComparison.OrdinalIgnoreCase)
                                          || p.Aliase.Any(a => string.Equals(a, feld.Name, StringComparison.OrdinalIgnoreCase)));
            if (!bekannt)
                logger?.Warning(Kopf + $"{name}: unbekannter Wert \"{feld.Name}\" wird ignoriert. Bekannt: {string.Join(", ", Werte.Select(p => p.Name))}.");
        }

        Liste(wurzel, "Ausnahmen", new[] { "Exclude", "Exceptions" }, e.Ausnahmen);
        Liste(wurzel, "NurDiese", new[] { "OnlyThese", "Only" }, e.NurDiese);
        return e;
    }

    private static JsonElement? Feld(JsonElement obj, string name, string[] aliase)
    {
        foreach (var f in obj.EnumerateObject())
        {
            if (string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)) return f.Value;
            foreach (var a in aliase)
                if (string.Equals(f.Name, a, StringComparison.OrdinalIgnoreCase)) return f.Value;
        }
        return null;
    }

    private static double? Zahl(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.GetDouble(),
        JsonValueKind.String when double.TryParse(e.GetString(), NumberStyles.Float, Inv, out var d) => d,
        _ => null
    };

    private static void Liste(JsonElement wurzel, string name, string[] aliase, HashSet<string> ziel)
    {
        if (Feld(wurzel, name, aliase) is not { ValueKind: JsonValueKind.Array } arr) return;
        foreach (var x in arr.EnumerateArray())
            if (x.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(x.GetString()))
                ziel.Add(x.GetString()!.Trim());
    }

    // ------------------------------------------------------------
    //  config.json anlegen (einmalige Uebernahme der alten Werte)
    // ------------------------------------------------------------

    private static string F(double d) => d.ToString("0.####", Inv);

    private static readonly JsonSerializerOptions Schreibweise = new()
    { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string J(string s) => JsonSerializer.Serialize(s, Schreibweise);

    private static string ModusName(Modus m) => m switch
    {
        Modus.Wert => "Wert", Modus.Plus => "Plus", Modus.Prozent => "Prozent", _ => "aus"
    };

    private static void Anlegen(string pfad, Einstellungen e, ISptLogger<BeimStart>? logger)
    {
        var sb = new StringBuilder();
        sb.Append("{\n");
        sb.Append("  \"_Anleitung\": [\n");
        sb.Append("    \"Gilt fuer ALLE Schalldaempfer gleichzeitig, auch fuer die aus anderen Mods (z. B. WTT-Armory). Aenderungen wirken beim naechsten Serverstart.\",\n");
        sb.Append("    \"Modus je Wert: aus = Originalwert | Wert = Zahl fest setzen | Plus = Original + Zahl | Prozent = Original x (1 + Zahl/100).\",\n");
        sb.Append("    \"Prozent rechnet mit dem Vorzeichen des Originals: Ergonomie -20 und Prozent -50 ergibt -10, Rueckstoss -6 und Prozent 50 ergibt -9.\",\n");
        sb.Append("    \"Original = der Wert ohne diese Mod, samt Aenderungen anderer Mods (z. B. SVM-Gewicht). Feste Zahlen von BetterSuppressors werden zurueckgenommen.\",\n");
        sb.Append("    \"Min und Max begrenzen das Ergebnis. Dezimalzahlen mit Punkt schreiben (0.9, nicht 0,9).\",\n");
        sb.Append("    \"Ausnahmen: Template-IDs, die ihre Originalwerte behalten. NurDiese: wenn nicht leer, werden NUR diese IDs angepasst.\"\n");
        sb.Append("  ],\n");
        sb.Append("  \"_Guide\": [\n");
        sb.Append("    \"Applies to ALL suppressors at once, including those added by other mods (e.g. WTT-Armory). Changes take effect on the next server start.\",\n");
        sb.Append("    \"Mode per value: aus = original value | Wert = set to Zahl | Plus = original + Zahl | Prozent = original x (1 + Zahl/100). English names work too: off, set, add, percent.\",\n");
        sb.Append("    \"Prozent keeps the sign of the original: ergonomics -20 with Prozent -50 gives -10, recoil -6 with Prozent 50 gives -9.\",\n");
        sb.Append("    \"Original = the value without this mod, including changes by other mods (e.g. SVM weight). Fixed numbers set by BetterSuppressors are undone.\",\n");
        sb.Append("    \"Min and Max clamp the result. Use a dot for decimals (0.9, not 0,9).\",\n");
        sb.Append("    \"Ausnahmen (exclude): template IDs that keep their original values. NurDiese (only these): if not empty, ONLY these IDs are changed.\"\n");
        sb.Append("  ],\n\n");
        sb.Append("  \"Parameter\": {\n");
        for (int i = 0; i < Werte.Length; i++)
        {
            var p = Werte[i];
            var r = e.Regeln[i];
            sb.Append($"    {J(p.Name)}: {{\n");
            sb.Append($"      \"Modus\": {J(ModusName(r.Modus))}, \"Zahl\": {F(r.Zahl)}, \"Min\": {F(r.Min)}, \"Max\": {F(r.Max)},\n");
            sb.Append($"      \"_de\": {J(p.De)},\n");
            sb.Append($"      \"_en\": {J(p.En)}\n");
            sb.Append(i < Werte.Length - 1 ? "    },\n" : "    }\n");
        }
        sb.Append("  },\n\n");
        sb.Append("  \"Ausnahmen\": [],\n");
        sb.Append("  \"NurDiese\": []\n");
        sb.Append("}\n");

        try
        {
            File.WriteAllText(pfad, sb.ToString(), new UTF8Encoding(false));
            logger?.Success(Kopf + $"config.json angelegt, Werte uebernommen aus: {e.Quelle}. Ab jetzt gilt nur noch diese Datei.");
        }
        catch (Exception ex)
        {
            logger?.Warning(Kopf + "config.json konnte nicht angelegt werden (" + ex.Message + ") - es gelten trotzdem die gelesenen Werte.");
        }
    }

    // ------------------------------------------------------------
    //  Anwenden
    // ------------------------------------------------------------

    public static double? Ergebnis(Parameter p, Regel r, double? original)
    {
        if (r.Modus == Modus.Aus) return original;
        double basis = original ?? p.Neutral;
        double neu = r.Modus switch
        {
            Modus.Wert => r.Zahl,
            Modus.Plus => basis + r.Zahl,
            Modus.Prozent => basis * (1.0 + r.Zahl / 100.0),
            _ => basis
        };
        neu = Math.Clamp(neu, r.Min, r.Max);
        if (p.Ganzzahl)
        {
            neu = Math.Round(neu, MidpointRounding.AwayFromZero);
            // Gerundet nicht ueber eine gebrochene Grenze hinaus.
            if (neu < r.Min) neu = Math.Ceiling(r.Min);
            if (neu > r.Max) neu = Math.Floor(r.Max);
        }
        else
        {
            neu = Math.Round(neu, 4);   // keine 0.30000000000000004
        }
        return neu;
    }

    public sealed class Bericht
    {
        public int Gesamt, Bearbeitet, Uebersprungen, Geaendert;
        public double[] VorMin = new double[Werte.Length], VorMax = new double[Werte.Length];
        public double[] NachMin = new double[Werte.Length], NachMax = new double[Werte.Length];
        public Bericht()
        {
            Array.Fill(VorMin, double.PositiveInfinity); Array.Fill(VorMax, double.NegativeInfinity);
            Array.Fill(NachMin, double.PositiveInfinity); Array.Fill(NachMax, double.NegativeInfinity);
        }
    }

    /// <summary>Setzt die Werte auf jede Schalldaempfervorlage, immer vom
    /// gesicherten Original aus. erledigt: Vorlagen, die schon einmal
    /// bearbeitet wurden (wird ergaenzt).</summary>
    public static Bericht Anwenden(TemplateTable vorlagen, Einstellungen e, HashSet<string> erledigt)
    {
        var b = new Bericht();

        lock (Sperre)
        {
            foreach (var (id, eig) in Schalldaempfer(vorlagen))
            {
                b.Gesamt++;
                if (e.Ausnahmen.Contains(id) || (e.NurDiese.Count > 0 && !e.NurDiese.Contains(id)))
                {
                    b.Uebersprungen++;
                    continue;
                }
                erledigt.Add(id);
                b.Bearbeitet++;

                var orig = Original(id, eig);
                bool weichtAb = false;
                for (int i = 0; i < Werte.Length; i++)
                {
                    var p = Werte[i];
                    var jetzt = p.Lesen(eig);
                    var ziel = Ergebnis(p, e.Regeln[i], orig[i]);
                    if (orig[i] is { } o) { b.VorMin[i] = Math.Min(b.VorMin[i], o); b.VorMax[i] = Math.Max(b.VorMax[i], o); }
                    if (ziel is { } z)
                    {
                        if (jetzt != z) p.Schreiben(eig, z);
                        b.NachMin[i] = Math.Min(b.NachMin[i], z); b.NachMax[i] = Math.Max(b.NachMax[i], z);
                    }
                    if (ziel != orig[i]) weichtAb = true;
                }
                if (weichtAb) b.Geaendert++;
            }
        }
        return b;
    }

    public static IEnumerable<string> Zeilen(Einstellungen e, Bericht b)
    {
        static string Bereich(double lo, double hi) =>
            double.IsInfinity(lo) ? "-" : (lo == hi ? F(lo) : $"{F(lo)} .. {F(hi)}");

        for (int i = 0; i < Werte.Length; i++)
        {
            var r = e.Regeln[i];
            string regel = r.Modus switch
            {
                Modus.Aus => "aus",
                Modus.Wert => $"Wert {F(r.Zahl)}",
                Modus.Plus => $"Plus {(r.Zahl >= 0 ? "+" : "")}{F(r.Zahl)}",
                Modus.Prozent => $"Prozent {(r.Zahl >= 0 ? "+" : "")}{F(r.Zahl)} %",
                _ => "?"
            };
            if (r.Modus != Modus.Aus) regel += $", Grenzen {F(r.Min)} .. {F(r.Max)}";
            yield return $"  {Werte[i].Name,-15} [{regel}]: Original {Bereich(b.VorMin[i], b.VorMax[i])}  ->  jetzt {Bereich(b.NachMin[i], b.NachMax[i])}"
                         + (BsZurueck[i] > 0 ? $"  (bei {BsZurueck[i]} die Zahl von BetterSuppressors zurueckgenommen)" : "");
        }
    }
}

/// <summary>Zeitpunkt a: Werte sichern, direkt VOR BetterSuppressors.</summary>
[Injectable(InjectionType.Singleton, TypePriority = Arbeit.BsPrioritaet - 1)]
public class VorBetterSuppressors(TemplateTable vorlagen) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken abbruch)
    {
        Arbeit.VorBsSichern(vorlagen);
        return Task.CompletedTask;
    }
}

/// <summary>Zeitpunkt a: direkt NACH BetterSuppressors dessen feste Zahlen
/// zuruecknehmen - bevor andere Mods Kopien davon anlegen.</summary>
[Injectable(InjectionType.Singleton, TypePriority = Arbeit.BsPrioritaet + 1)]
public class NachBetterSuppressors(TemplateTable vorlagen) : IOnLoad
{
    public Task OnLoadAsync(CancellationToken abbruch)
    {
        Arbeit.BsZuruecknehmen(vorlagen);
        return Task.CompletedTask;
    }
}

/// <summary>Zeitpunkt b: beim Serverstart, als letzter IOnLoad.</summary>
[Injectable(InjectionType.Singleton)]
public class BeimStart(
    ISptLogger<BeimStart> logger,
    TemplateTable vorlagen) : IOnLoad
{
    internal static readonly HashSet<string> Erledigt = new();

    public Task OnLoadAsync(CancellationToken abbruch)
    {
        try
        {
            var e = Arbeit.Einstellungen(logger);
            var b = Arbeit.Anwenden(vorlagen, e, Erledigt);

            if (b.Gesamt == 0)
            {
                logger.Warning("[Schalldaempfer fuer alle] Keine Schalldaempfervorlagen "
                               + "gefunden - hat sich die Basisklasse geaendert?");
                return Task.CompletedTask;
            }

            logger.Success($"[Schalldaempfer fuer alle] Start: {b.Gesamt} Schalldaempfer, {b.Bearbeitet} angepasst"
                           + (b.Uebersprungen > 0 ? $", {b.Uebersprungen} ausgenommen" : "")
                           + $" ({b.Geaendert} davon weichen jetzt vom Original ab). Quelle: {e.Quelle}.");
            foreach (var z in Arbeit.Zeilen(e, b)) logger.Info("[Schalldaempfer fuer alle] " + z);

            if (Arbeit.BsVorlagen > 0)
                logger.Info($"[Schalldaempfer fuer alle] BetterSuppressors hatte {Arbeit.BsVorlagen} Schalldaempfer fest gesetzt - "
                            + "gleich danach zurueckgenommen, gerechnet wird mit den Werten von davor. "
                            + "Neben dieser Mod ist BetterSuppressors ueberfluessig.");
            else if (Arbeit.BetterSuppressorsDa)
                logger.Warning("[Schalldaempfer fuer alle] BetterSuppressors ist installiert, aber seine Aenderungen waren "
                               + "nicht zu erkennen (andere Version/Prioritaet?). Plus/Prozent rechnen dann auf seine Zahlen.");
        }
        catch (Exception ex)
        {
            logger.Error("[Schalldaempfer fuer alle] " + ex.Message);
        }

        return Task.CompletedTask;
    }
}

/// <summary>Zeitpunkt c: einmalig bei der ersten Client-Anfrage, damit
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
                int vorher = BeimStart.Erledigt.Count;
                var e = Arbeit.Einstellungen(null);
                var b = Arbeit.Anwenden(vorlagen, e, BeimStart.Erledigt);
                int neu = BeimStart.Erledigt.Count - vorher;

                if (neu > 0)
                {
                    logger.Success($"[Schalldaempfer fuer alle] Nachgezogen: {neu} von {b.Gesamt} "
                                   + "Schalldaempfern kamen erst nach dem Serverstart dazu und sind jetzt auch angepasst.");
                    foreach (var z in Arbeit.Zeilen(e, b)) logger.Info("[Schalldaempfer fuer alle] " + z);
                }
                else
                {
                    logger.Info($"[Schalldaempfer fuer alle] Nachgezogen: nichts offen, "
                                + $"alle {b.Bearbeitet} waren schon angepasst.");
                }

                // Listen-IDs, die es gar nicht gibt (Tippfehler?)
                var bekannt = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (id, v) in vorlagen.Items)
                    if (v.Parent == Arbeit.SchalldaempferBasis) bekannt.Add(id.ToString());
                var fremd = e.Ausnahmen.Concat(e.NurDiese).Where(id => !bekannt.Contains(id)).ToList();
                if (fremd.Count > 0)
                    logger.Warning("[Schalldaempfer fuer alle] Diese IDs aus Ausnahmen/NurDiese sind keine Schalldaempfer: "
                                   + string.Join(", ", fremd));
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

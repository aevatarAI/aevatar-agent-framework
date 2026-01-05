/*aevatar_tool
{
  "name": "bazi_chart",
  "description": "Compute BaZi (Four Pillars) using: Lichun boundary for year, solar longitude segments for month, true solar time for hour, and ZiChu(23:00) for day rollover. Hour-level precision is enough.",
  "category": "Core",
  "version": "1.0.0",
  "tags": ["dotnet", "file", "skill", "bazi", "ganzhi", "calendar"],
  "parameters": {
    "required": ["birth"],
    "items": {
      "birth": { "type": "string", "description": "Birth local time, e.g. '1991-07-15 13' or '1991-07-15 13:20' (hour-level is enough)" },
      "tz": { "type": "string", "description": "IANA timezone id (default: Asia/Shanghai)" },
      "lon": { "type": "number", "description": "Longitude (east positive). Default: Beijing 116.4074" },
      "lat": { "type": "number", "description": "Latitude (north positive). Default: Beijing 39.9042 (currently unused in this approximation)" }
    }
  }
}
*/

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;

// ============================================================
//  八字排盘（参考 examples/SkillsMCPUnifiedDemo/refs/bazi.py）
//
//  口径（与 bazi.py 对齐）：
//  1) 年柱：立春（定气，太阳视黄经=315°）为边界换年柱
//  2) 月柱：按“节”（12节）换月（用太阳视黄经每 30° 一段，寅月从 315° 开始）
//  3) 时柱：用真太阳时（True Solar Time）
//  4) 日柱：子初换日（真太阳时 >= 23:00 视为次日）
//
//  NOTE:
//  - 太阳位置/EoT 使用 NOAA/Meeus 常用近似（到“时辰级别”一般足够）。
//  - 这个工具只做“四柱”，不做大运/起运（需要时可再扩展）。
// ============================================================

var jsonOptions = new JsonSerializerOptions
{
    // .NET 10 file-based apps may disable reflection serialization by default.
    TypeInfoResolver = new DefaultJsonTypeInfoResolver()
};

var input = await Console.In.ReadToEndAsync();
var inputArgs = string.IsNullOrWhiteSpace(input)
    ? new Dictionary<string, JsonElement>()
    : JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(input, jsonOptions)
      ?? new Dictionary<string, JsonElement>();

var birthRaw = GetString(inputArgs, "birth");
if (string.IsNullOrWhiteSpace(birthRaw))
{
    Fail("Parameter 'birth' is required. Example: '1991-07-15 13' (hour-level is enough).");
    return;
}

var tzId = GetString(inputArgs, "tz");
if (string.IsNullOrWhiteSpace(tzId))
    tzId = "Asia/Shanghai";

// Default: Beijing
var lonDeg = GetDouble(inputArgs, "lon", 116.4074);
var latDeg = GetDouble(inputArgs, "lat", 39.9042);

TimeZoneInfo tz;
try
{
    tz = TimeZoneInfo.FindSystemTimeZoneById(tzId);
}
catch
{
    // Best-effort fallback; still usable for hour-level in most cases.
    tz = TimeZoneInfo.Local;
    tzId = tz.Id;
}

if (!TryParseLocalDateTime(birthRaw, out var birthLocal))
{
    Fail("Invalid 'birth' format. Supported examples: '1991-07-15 13', '1991-07-15 13:20', '1991-07-15 13:20:00'.");
    return;
}

var offset = tz.GetUtcOffset(birthLocal);
var birthLocalDto = new DateTimeOffset(DateTime.SpecifyKind(birthLocal, DateTimeKind.Unspecified), offset);
var birthUtc = birthLocalDto.UtcDateTime;

// ---- Solar longitude + EoT at birth ----
var jd = JulianDay(birthUtc);
var solarLon = SunApparentLongitudeDeg(jd);
var eotMin = EquationOfTimeMinutes(jd);

// ---- True solar time label (date + minutes) ----
var solar = TrueSolarLabel(birthLocal, tz, lonDeg, eotMin);
var effDate = solar.SolarDate.AddDays(solar.SolarMinutes >= 1380.0 ? 1 : 0); // >= 23:00 => next day

// ---- Year pillar (Lichun boundary) ----
var (yearForGz, lichunUtc) = YearForGanzhiByLiChun(birthUtc, birthLocal.Year);
var yGz = YearGanzhi(yearForGz);

// ---- Month pillar (solar longitude segments, 寅月起) ----
var mGz = MonthGanzhi(yGz.StemIndex, solarLon);

// ---- Day pillar (effective date by ZiChu) ----
var dGz = DayGanzhiByEffectiveDate(effDate);

// ---- Hour pillar (true solar time) ----
var hGz = HourGanzhi(dGz.StemIndex, solar.SolarMinutes);

var result = new
{
    success = true,
    input = new
    {
        birth = birthRaw.Trim(),
        tz = tzId,
        lon = lonDeg,
        lat = latDeg
    },
    normalized = new
    {
        birthLocal = birthLocalDto.ToString("yyyy-MM-dd HH:mm:ss zzz", CultureInfo.InvariantCulture),
        birthUtc = birthUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture)
    },
    astronomical = new
    {
        sunLongitudeDeg = Round6(solarLon),
        equationOfTimeMinutes = Round3(eotMin),
        trueSolar = new
        {
            solarDate = solar.SolarDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            solarTime = MinutesToHhmm(solar.SolarMinutes),
            solarMinutes = Round3(solar.SolarMinutes)
        },
        dayRollover = new
        {
            rule = "ZiChu: true solar time >= 23:00 -> next day",
            effectiveDate = effDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        },
        lichun = new
        {
            rule = "Lichun (SunLon=315°) switches year pillar",
            lichunUtc = lichunUtc.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture),
            yearForGanzhi = yearForGz
        }
    },
    pillars = new
    {
        year = yGz.Text,
        month = mGz.Text,
        day = dGz.Text,
        hour = hGz.Text
    }
};

Console.WriteLine(JsonSerializer.Serialize(result, jsonOptions));

// ============================================================
//  Core: Ganzhi + Calendar Helpers
// ============================================================

static (int YearForGanzhi, DateTime LichunUtc) YearForGanzhiByLiChun(DateTime birthUtc, int birthGregorianYear)
{
    // Lichun: SunLon reaches 315°
    var start = new DateTime(birthGregorianYear, 1, 15, 0, 0, 0, DateTimeKind.Utc);
    var lichun = FindNextLongitudeTimeUtc(315.0, start);
    var yearForGz = birthUtc >= lichun ? birthGregorianYear : birthGregorianYear - 1;
    return (yearForGz, lichun);
}

static Ganzhi YearGanzhi(int yearForGz)
{
    // 4 AD is treated as 甲子 start (common formula)
    var stem = Mod(yearForGz - 4, 10);
    var branch = Mod(yearForGz - 4, 12);
    return new Ganzhi(stem, branch);
}

static Ganzhi MonthGanzhi(int yearStemIndex, double solarLonDeg)
{
    var mBranch = MonthBranchFromSolarLongitude(solarLonDeg);

    // Convert branch index to month number (寅月=1 ... 丑月=12)
    var monthNumber = Mod(mBranch - 2, 12) + 1;

    // 寅月天干：((年干 mod 5) * 2 + 2) mod 10
    var yinStem = Mod((yearStemIndex % 5) * 2 + 2, 10);
    var mStem = Mod(yinStem + (monthNumber - 1), 10);

    return new Ganzhi(mStem, mBranch);
}

static int MonthBranchFromSolarLongitude(double lonDeg)
{
    // ------------------------------------------------------------
    // 设计：消灭特殊情况
    // 把 [0,360) 在 315° 处“切开并展开”为连续轴 [315,675)：
    //   - lon>=315 直接用 lon
    //   - lon<315  视作 lon+360
    // 这样 12 段都是统一的 30° 等分：
    //   寅:[315,345) 卯:[345,375) ... 丑:[645,675)
    // ------------------------------------------------------------
    var lon = DegNorm(lonDeg);
    var lon2 = lon >= 315.0 ? lon : lon + 360.0;
    var monthNo = (int)((lon2 - 315.0) / 30.0) + 1; // 1..12, 寅月为 1

    // 月支：寅月起 -> 寅(2), 卯(3), ..., 子(0), 丑(1)
    return Mod(monthNo + 1, 12);
}

static Ganzhi DayGanzhiByEffectiveDate(DateOnly effDate)
{
    // Base: 1900-01-31 is 甲辰 (index 40)
    var baseDate = new DateOnly(1900, 1, 31);
    const int baseIndex = 40;
    var offset = effDate.DayNumber - baseDate.DayNumber;
    var idx = Mod(baseIndex + offset, 60);
    return new Ganzhi(Mod(idx, 10), Mod(idx, 12));
}

static Ganzhi HourGanzhi(int dayStemIndex, double solarMinutes)
{
    // 子时：[23:00, 01:00) ...  (shift by +60 min then /120)
    var hBranch = Mod((int)((solarMinutes + 60.0) / 120.0), 12);

    // 子时天干：((日干 mod 5) * 2) mod 10
    var ziStem = Mod((dayStemIndex % 5) * 2, 10);
    var hStem = Mod(ziStem + hBranch, 10);
    return new Ganzhi(hStem, hBranch);
}

// ============================================================
//  Core: True Solar Time (EoT + longitude)
// ============================================================

static SolarLabel TrueSolarLabel(DateTime birthLocal, TimeZoneInfo tz, double lonDeg, double eotMinutes)
{
    var tzHours = tz.GetUtcOffset(birthLocal).TotalHours;
    var localMinutes = birthLocal.TimeOfDay.TotalMinutes;

    // TrueSolarMinutes = LocalMinutes + EoT + 4*Lon - 60*TZ
    var raw = localMinutes + eotMinutes + 4.0 * lonDeg - 60.0 * tzHours;

    var dayOffset = (int)Math.Floor(raw / 1440.0);
    var minutes = raw - dayOffset * 1440.0;
    if (minutes < 0) minutes += 1440.0;

    var solarDate = DateOnly.FromDateTime(birthLocal).AddDays(dayOffset);
    return new SolarLabel(solarDate, minutes, eotMinutes);
}

// ============================================================
//  Astronomy: JD / Sun longitude / Equation of Time (NOAA approx)
// ============================================================

static DateTime FindNextLongitudeTimeUtc(double targetDeg, DateTime startUtc)
{
    if (startUtc.Kind != DateTimeKind.Utc)
        startUtc = DateTime.SpecifyKind(startUtc, DateTimeKind.Utc);

    var jd0 = JulianDay(startUtc);
    var lon0 = SunApparentLongitudeDeg(jd0);
    var targetU = targetDeg >= lon0 ? targetDeg : targetDeg + 360.0;

    var lo = startUtc;
    var hi = startUtc.AddDays(40);

    double LonU(DateTime dtUtc)
    {
        var lon = SunApparentLongitudeDeg(JulianDay(dtUtc));
        return lon < lon0 ? lon + 360.0 : lon;
    }

    while (LonU(hi) < targetU)
        hi = hi.AddDays(20);

    for (var i = 0; i < 80; i++)
    {
        var mid = lo + TimeSpan.FromTicks((hi - lo).Ticks / 2);
        if (LonU(mid) < targetU) lo = mid;
        else hi = mid;
        if ((hi - lo).TotalSeconds <= 30) break; // hour-level is enough
    }

    return hi;
}

static double JulianDay(DateTime utc)
{
    if (utc.Kind != DateTimeKind.Utc)
        utc = DateTime.SpecifyKind(utc, DateTimeKind.Utc);

    var y = utc.Year;
    var m = utc.Month;
    var d = utc.Day;

    var frac = utc.TimeOfDay.TotalSeconds / 86400.0;

    if (m <= 2)
    {
        y -= 1;
        m += 12;
    }

    var a = y / 100;
    var b = 2 - a + a / 4;

    var jd =
        (int)(365.25 * (y + 4716))
        + (int)(30.6001 * (m + 1))
        + d
        + b
        - 1524.5
        + frac;

    return jd;
}

static double SunApparentLongitudeDeg(double jd)
{
    var t = (jd - 2451545.0) / 36525.0;

    var l0 = DegNorm(280.46646 + 36000.76983 * t + 0.0003032 * t * t);
    var m = DegNorm(357.52911 + 35999.05029 * t - 0.0001537 * t * t);

    var c =
        (1.914602 - 0.004817 * t - 0.000014 * t * t) * Math.Sin(Rad(m))
        + (0.019993 - 0.000101 * t) * Math.Sin(Rad(2 * m))
        + 0.000289 * Math.Sin(Rad(3 * m));

    var trueLong = l0 + c;
    var omega = DegNorm(125.04 - 1934.136 * t);
    var lam = trueLong - 0.00569 - 0.00478 * Math.Sin(Rad(omega));
    return DegNorm(lam);
}

static double EquationOfTimeMinutes(double jd)
{
    var t = (jd - 2451545.0) / 36525.0;

    var l0 = DegNorm(280.46646 + 36000.76983 * t + 0.0003032 * t * t);
    var m = DegNorm(357.52911 + 35999.05029 * t - 0.0001537 * t * t);
    var e = 0.016708634 - 0.000042037 * t - 0.0000001267 * t * t;

    var eps0 = 23.0 + (26.0 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60.0) / 60.0;
    var omega = DegNorm(125.04 - 1934.136 * t);
    var eps = eps0 + 0.00256 * Math.Cos(Rad(omega));

    var y = Math.Tan(Rad(eps) / 2.0);
    y *= y;

    var l0r = Rad(l0);
    var mr = Rad(m);

    var eot =
        y * Math.Sin(2 * l0r)
        - 2 * e * Math.Sin(mr)
        + 4 * e * y * Math.Sin(mr) * Math.Cos(2 * l0r)
        - 0.5 * y * y * Math.Sin(4 * l0r)
        - 1.25 * e * e * Math.Sin(2 * mr);

    return 4.0 * Deg(eot);
}

// ============================================================
//  Small utils
// ============================================================

static bool TryParseLocalDateTime(string s, out DateTime local)
{
    s = (s ?? string.Empty).Trim().Replace('T', ' ');
    var fmts = new[]
    {
        "yyyy-MM-dd HH",
        "yyyy-MM-dd H",
        "yyyy-MM-dd HH:mm",
        "yyyy-MM-dd H:mm",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy-MM-dd H:mm:ss"
    };

    return DateTime.TryParseExact(
        s,
        fmts,
        CultureInfo.InvariantCulture,
        DateTimeStyles.AllowWhiteSpaces,
        out local);
}

static string GetString(Dictionary<string, JsonElement> args, string key)
{
    if (!args.TryGetValue(key, out var v)) return string.Empty;
    if (v.ValueKind == JsonValueKind.String) return v.GetString() ?? string.Empty;
    return v.ToString() ?? string.Empty;
}

static double GetDouble(Dictionary<string, JsonElement> args, string key, double defaultValue)
{
    if (!args.TryGetValue(key, out var v)) return defaultValue;

    try
    {
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble(),
            JsonValueKind.String when double.TryParse(v.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
            _ => defaultValue
        };
    }
    catch
    {
        return defaultValue;
    }
}

static void Fail(string error)
{
    var jsonOptions = new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() };
    Console.WriteLine(JsonSerializer.Serialize(new { success = false, error }, jsonOptions));
}

static double DegNorm(double x)
{
    x %= 360.0;
    return x < 0 ? x + 360.0 : x;
}

static double Rad(double deg) => Math.PI / 180.0 * deg;
static double Deg(double rad) => 180.0 / Math.PI * rad;

static int Mod(int a, int m)
{
    var r = a % m;
    return r < 0 ? r + m : r;
}

static string MinutesToHhmm(double minutes)
{
    var sec = (int)Math.Round(minutes * 60.0);
    sec %= 86400;
    if (sec < 0) sec += 86400;
    var h = sec / 3600;
    var m = (sec % 3600) / 60;
    return $"{h:00}:{m:00}";
}

static double Round6(double v) => Math.Round(v, 6, MidpointRounding.AwayFromZero);
static double Round3(double v) => Math.Round(v, 3, MidpointRounding.AwayFromZero);

readonly record struct SolarLabel(DateOnly SolarDate, double SolarMinutes, double EquationOfTimeMinutes);

readonly record struct Ganzhi(int StemIndex, int BranchIndex)
{
    private static readonly string[] Stems = ["甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸"];
    private static readonly string[] Branches = ["子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥"];

    public string Text => $"{Stems[StemIndex]}{Branches[BranchIndex]}";
}



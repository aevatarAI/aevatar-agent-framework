#!/usr/bin/env python3
"""
八字排盘（偏“协议/降频”视角的一套自洽口径）

口径（与你在对话里确认的一致）：
1) 年柱：立春（定气）为边界换年柱
2) 月柱：按“节”（12 节）换月（定气：太阳视黄经每到固定角度）
3) 时柱：用真太阳时（True Solar Time）
4) 日柱：子初换日（真太阳时 >= 23:00 视为次日）
5) 起运：顺逆由“年干阴阳 + 性别”决定；取顺逆方向上的最近“节”边界，三天一岁
6) 换运：每 10 年一换（按回归年长度换算为时间）

注意：
- 这里的天文部分使用 NOAA/Meeus 常用近似（到“分级”精度一般足够）。如果你要把边界压到秒级，
  需要引入更高精度的太阳视黄经模型（比如 VSOP87）或权威历表。
"""

from __future__ import annotations

import argparse
import math
from dataclasses import dataclass
from datetime import date, datetime, timedelta, timezone
from zoneinfo import ZoneInfo


STEMS = ["甲", "乙", "丙", "丁", "戊", "己", "庚", "辛", "壬", "癸"]
BRANCHES = ["子", "丑", "寅", "卯", "辰", "巳", "午", "未", "申", "酉", "戌", "亥"]

# ------------------------------------------------------------
# 节气（只取 12 个“节”，用于月柱边界与起运边界）
# 角度：太阳视黄经（apparent ecliptic longitude），单位：deg
# ------------------------------------------------------------
JIE = [
    ("立春", 315.0),
    ("惊蛰", 345.0),
    ("清明", 15.0),
    ("立夏", 45.0),
    ("芒种", 75.0),
    ("小暑", 105.0),
    ("立秋", 135.0),
    ("白露", 165.0),
    ("寒露", 195.0),
    ("立冬", 225.0),
    ("大雪", 255.0),
    ("小寒", 285.0),
]

TROPICAL_YEAR_DAYS = 365.242189  # 回归年（tropical year）近似


@dataclass(frozen=True)
class Ganzhi:
    stem_index: int
    branch_index: int

    def stem(self) -> str:
        return STEMS[self.stem_index]

    def branch(self) -> str:
        return BRANCHES[self.branch_index]

    def text(self) -> str:
        return f"{self.stem()}{self.branch()}"


@dataclass(frozen=True)
class SolarLabel:
    """
    一个“标签时间”：描述同一物理瞬间在当地的真太阳时读数（含日期/时间）。
    """

    solar_date: date
    solar_minutes: float  # [0, 1440)
    equation_of_time_minutes: float

    def hhmmss(self) -> str:
        sec = int(round(self.solar_minutes * 60))
        sec %= 86400
        h = sec // 3600
        m = (sec % 3600) // 60
        s = sec % 60
        return f"{h:02d}:{m:02d}:{s:02d}"


# ============================================================
# 基础数学 / 角度工具
# ============================================================


def _deg_norm(x: float) -> float:
    x = x % 360.0
    return x + 360.0 if x < 0 else x


def _rad(x_deg: float) -> float:
    return math.radians(x_deg)


def _deg(x_rad: float) -> float:
    return math.degrees(x_rad)


# ============================================================
# 儒略日（JD）与太阳近似位置
# ============================================================


def julian_day(dt_utc: datetime) -> float:
    """
    计算 UTC 时刻对应的儒略日 JD（天文学常用连续日）。
    """
    if dt_utc.tzinfo is None:
        raise ValueError("julian_day: dt_utc 必须是 timezone-aware 的 UTC datetime")
    dt_utc = dt_utc.astimezone(timezone.utc)

    y = dt_utc.year
    m = dt_utc.month
    d = dt_utc.day

    # 把一天的小数部分加进去
    frac = (
        dt_utc.hour / 24.0
        + dt_utc.minute / 1440.0
        + dt_utc.second / 86400.0
        + dt_utc.microsecond / 86400.0 / 1_000_000.0
    )

    if m <= 2:
        y -= 1
        m += 12

    a = y // 100
    b = 2 - a + (a // 4)

    jd = (
        int(365.25 * (y + 4716))
        + int(30.6001 * (m + 1))
        + d
        + b
        - 1524.5
        + frac
    )
    return float(jd)


def sun_apparent_longitude_deg(jd: float) -> float:
    """
    太阳视黄经（apparent ecliptic longitude），近似算法（NOAA/Meeus 常用式）。
    输出：deg in [0, 360).
    """
    t = (jd - 2451545.0) / 36525.0  # Julian centuries since J2000.0

    # Mean longitude
    l0 = _deg_norm(280.46646 + 36000.76983 * t + 0.0003032 * t * t)

    # Mean anomaly
    m = _deg_norm(357.52911 + 35999.05029 * t - 0.0001537 * t * t)

    # Equation of center
    c = (
        (1.914602 - 0.004817 * t - 0.000014 * t * t) * math.sin(_rad(m))
        + (0.019993 - 0.000101 * t) * math.sin(_rad(2 * m))
        + 0.000289 * math.sin(_rad(3 * m))
    )

    true_long = l0 + c

    omega = _deg_norm(125.04 - 1934.136 * t)
    lam = true_long - 0.00569 - 0.00478 * math.sin(_rad(omega))
    return _deg_norm(lam)


def equation_of_time_minutes(jd: float) -> float:
    """
    均时差（Equation of Time），单位：分钟。
    NOAA 近似公式，足以用于真太阳时换算。
    """
    t = (jd - 2451545.0) / 36525.0

    l0 = _deg_norm(280.46646 + 36000.76983 * t + 0.0003032 * t * t)
    m = _deg_norm(357.52911 + 35999.05029 * t - 0.0001537 * t * t)
    e = 0.016708634 - 0.000042037 * t - 0.0000001267 * t * t

    # Mean obliquity
    eps0 = 23.0 + (26.0 + (21.448 - t * (46.815 + t * (0.00059 - t * 0.001813))) / 60.0) / 60.0
    omega = _deg_norm(125.04 - 1934.136 * t)
    eps = eps0 + 0.00256 * math.cos(_rad(omega))

    y = math.tan(_rad(eps) / 2.0)
    y = y * y

    l0r = _rad(l0)
    mr = _rad(m)

    eot = (
        y * math.sin(2 * l0r)
        - 2 * e * math.sin(mr)
        + 4 * e * y * math.sin(mr) * math.cos(2 * l0r)
        - 0.5 * y * y * math.sin(4 * l0r)
        - 1.25 * e * e * math.sin(2 * mr)
    )

    return 4.0 * _deg(eot)


# ============================================================
# 定气：求太阳黄经到达某个角度的时刻（bisection）
# ============================================================


def _unwrap_lon_forward(lon_deg: float, lon0_deg: float) -> float:
    """
    给定区间起点 lon0（0..360），把 lon 映射到 [lon0, lon0+360) 上的连续表示。
    """
    return lon_deg + (360.0 if lon_deg < lon0_deg else 0.0)


def _unwrap_lon_backward(lon_deg: float, lon_end_deg: float) -> float:
    """
    给定区间终点 lon_end（0..360），把 lon 映射到 (lon_end-360, lon_end] 上的连续表示。
    """
    return lon_deg - (360.0 if lon_deg > lon_end_deg else 0.0)


def find_next_longitude_time_utc(target_deg: float, start_dt_utc: datetime) -> datetime:
    """
    从 start_dt_utc（含）起，找太阳视黄经到达 target_deg 的下一次时刻（UTC）。
    """
    if start_dt_utc.tzinfo is None:
        raise ValueError("start_dt_utc 必须是 timezone-aware")
    start_dt_utc = start_dt_utc.astimezone(timezone.utc)

    jd0 = julian_day(start_dt_utc)
    lon0 = sun_apparent_longitude_deg(jd0)
    target_u = target_deg if target_deg >= lon0 else target_deg + 360.0

    lo = start_dt_utc
    hi = start_dt_utc + timedelta(days=40)

    def lon_u(dt: datetime) -> float:
        lon = sun_apparent_longitude_deg(julian_day(dt))
        return _unwrap_lon_forward(lon, lon0)

    # 括住根：保证 hi 处已经越过 target_u
    while lon_u(hi) < target_u:
        hi += timedelta(days=20)

    # 二分
    for _ in range(80):
        mid = lo + (hi - lo) / 2
        if lon_u(mid) < target_u:
            lo = mid
        else:
            hi = mid
        if (hi - lo).total_seconds() <= 0.5:
            break

    return hi


def find_prev_longitude_time_utc(target_deg: float, end_dt_utc: datetime) -> datetime:
    """
    在 end_dt_utc（含）之前，找太阳视黄经到达 target_deg 的上一次时刻（UTC）。
    """
    if end_dt_utc.tzinfo is None:
        raise ValueError("end_dt_utc 必须是 timezone-aware")
    end_dt_utc = end_dt_utc.astimezone(timezone.utc)

    jd_end = julian_day(end_dt_utc)
    lon_end = sun_apparent_longitude_deg(jd_end)
    target_u = lon_end - ((lon_end - target_deg + 360.0) % 360.0)

    hi = end_dt_utc
    lo = end_dt_utc - timedelta(days=40)

    def lon_u(dt: datetime) -> float:
        lon = sun_apparent_longitude_deg(julian_day(dt))
        return _unwrap_lon_backward(lon, lon_end)

    while lon_u(lo) > target_u:
        lo -= timedelta(days=20)

    for _ in range(80):
        mid = lo + (hi - lo) / 2
        if lon_u(mid) < target_u:
            lo = mid
        else:
            hi = mid
        if (hi - lo).total_seconds() <= 0.5:
            break

    return hi


# ============================================================
# 真太阳时标签（用于时柱与子初换日）
# ============================================================


def true_solar_label(dt_local: datetime, lon_deg: float) -> SolarLabel:
    """
    计算同一物理瞬间在当地的真太阳时读数（日期+时刻），返回 SolarLabel。
    公式（NOAA 常用）：
      TrueSolarMinutes = LocalMinutes + EoT + 4*Lon - 60*TZ
    其中 Lon 为东经正，TZ 为 UTC 偏移小时（例如中国为 +8）。
    """
    if dt_local.tzinfo is None:
        raise ValueError("dt_local 必须是 timezone-aware（带时区）")

    dt_utc = dt_local.astimezone(timezone.utc)
    jd = julian_day(dt_utc)
    eot = equation_of_time_minutes(jd)

    tz_hours = dt_local.utcoffset().total_seconds() / 3600.0
    local_minutes = (
        dt_local.hour * 60.0
        + dt_local.minute
        + dt_local.second / 60.0
        + dt_local.microsecond / 60.0 / 1_000_000.0
    )

    raw = local_minutes + eot + 4.0 * lon_deg - 60.0 * tz_hours
    day_offset = math.floor(raw / 1440.0)
    minutes = raw - day_offset * 1440.0

    solar_date = dt_local.date() + timedelta(days=int(day_offset))
    return SolarLabel(solar_date=solar_date, solar_minutes=minutes, equation_of_time_minutes=eot)


# ============================================================
# 八字：年/月/日/时 四柱
# ============================================================


def year_ganzhi_by_lichun(birth_dt_utc: datetime, year: int) -> tuple[int, datetime]:
    """
    给定 birth_dt_utc 与其公历 year，计算当年立春时刻，
    并返回 “立春换年柱”下应该使用的干支年份 year_for_gz。
    """
    # 立春：太阳黄经到 315°
    start = datetime(year, 1, 15, 0, 0, 0, tzinfo=timezone.utc)
    lichun_utc = find_next_longitude_time_utc(315.0, start)

    year_for_gz = year if birth_dt_utc >= lichun_utc else year - 1
    return year_for_gz, lichun_utc


def year_ganzhi(year_for_gz: int) -> Ganzhi:
    # 4 AD 为甲子起点（常用公式）
    stem = (year_for_gz - 4) % 10
    branch = (year_for_gz - 4) % 12
    return Ganzhi(stem, branch)


def month_branch_from_solar_longitude(lon_deg: float) -> int:
    """
    按 12 节的边界把太阳黄经切成 12 段，返回月支（branch index）。

    月序（寅月起）：寅 卯 辰 巳 午 未 申 酉 戌 亥 子 丑
    边界（节）：立春315, 惊蛰345, 清明15, 立夏45, ...
    """
    lon = _deg_norm(lon_deg)

    # ------------------------------------------------------------
    # 设计：消灭特殊情况
    # 把 [0,360) 在 315° 处“切开并展开”为连续轴 [315,675)：
    #   - lon>=315 直接用 lon
    #   - lon<315  视作 lon+360
    # 这样 12 段都是统一的 30° 等分：
    #   寅:[315,345) 卯:[345,375) ... 丑:[645,675)
    # ------------------------------------------------------------
    lon2 = lon if lon >= 315.0 else lon + 360.0
    month_no = int((lon2 - 315.0) // 30.0) + 1  # 1..12, 寅月为 1

    # 月支：寅月起 -> 寅(2), 卯(3), ..., 子(0), 丑(1)
    return (month_no + 1) % 12


def month_ganzhi(year_stem_index: int, solar_lon_deg: float) -> Ganzhi:
    m_branch = month_branch_from_solar_longitude(solar_lon_deg)

    # 寅月为 1：寅(2)->1, 卯(3)->2, ..., 丑(1)->12
    month_number = (m_branch - 2) % 12 + 1

    # 寅月天干：((年干 mod 5) * 2 + 2) mod 10
    yin_stem = ((year_stem_index % 5) * 2 + 2) % 10
    m_stem = (yin_stem + (month_number - 1)) % 10
    return Ganzhi(m_stem, m_branch)


def day_ganzhi_by_effective_date(eff_date: date) -> Ganzhi:
    """
    日柱：按“子初(23:00)换日”的 effective date 计算。

    这里采用一份在开源实现里极常见的基准：
    - 公历 1900-01-31 为 甲辰 日（索引 40）
    - 任意日期的日干支索引 = (40 + days_since_1900_01_31) mod 60
    """
    base = date(1900, 1, 31)
    base_index = 40  # 甲辰
    offset = (eff_date - base).days
    idx = (base_index + offset) % 60
    return Ganzhi(idx % 10, idx % 12)


def hour_ganzhi(day_stem_index: int, solar_minutes: float) -> Ganzhi:
    """
    时柱：真太阳时定时辰。
    子时：[23:00, 01:00), 丑时：[01:00, 03:00) ...
    """
    h_branch = int((solar_minutes + 60.0) // 120.0) % 12

    # 子时天干：((日干 mod 5) * 2) mod 10
    zi_stem = ((day_stem_index % 5) * 2) % 10
    h_stem = (zi_stem + h_branch) % 10
    return Ganzhi(h_stem, h_branch)


# ============================================================
# 起运 / 大运
# ============================================================


def is_yang_stem(stem_index: int) -> bool:
    # 甲(0)为阳，乙(1)为阴，交替
    return (stem_index % 2) == 0


def next_prev_jie_by_lon(lon_deg: float) -> tuple[tuple[str, float], tuple[str, float]]:
    lon = _deg_norm(lon_deg)

    best_next = None
    best_prev = None

    for name, deg in JIE:
        dn = (deg - lon + 360.0) % 360.0
        dp = (lon - deg + 360.0) % 360.0

        if best_next is None or dn < best_next[2]:
            best_next = (name, deg, dn)
        if best_prev is None or dp < best_prev[2]:
            best_prev = (name, deg, dp)

    assert best_next is not None and best_prev is not None
    return (best_next[0], best_next[1]), (best_prev[0], best_prev[1])


def ganzhi_index_60(gz: Ganzhi) -> int:
    """
    把一个有效的干支对映射到 0..59（甲子为 0）。
    """
    for i in range(60):
        if (i % 10) == gz.stem_index and (i % 12) == gz.branch_index:
            return i
    raise ValueError(f"invalid ganzhi pair: {gz.text()}")


def compute_dayun(
    birth_dt_local: datetime,
    birth_dt_utc: datetime,
    sex: str,
    lon_deg: float,
    year_gz: Ganzhi,
    month_gz: Ganzhi,
    count: int,
) -> dict:
    """
    计算起运与若干步大运。
    返回一个 dict，方便打印/JSON。
    """
    solar_lon = sun_apparent_longitude_deg(julian_day(birth_dt_utc))
    (next_name, next_deg), (prev_name, prev_deg) = next_prev_jie_by_lon(solar_lon)

    male = sex.lower() in {"m", "male", "man", "男"}
    forward = (male == is_yang_stem(year_gz.stem_index))

    if forward:
        boundary_name, boundary_deg = next_name, next_deg
        boundary_utc = (
            birth_dt_utc
            if (boundary_deg - solar_lon + 360.0) % 360.0 == 0.0
            else find_next_longitude_time_utc(boundary_deg, birth_dt_utc)
        )
        delta_seconds = (boundary_utc - birth_dt_utc).total_seconds()
    else:
        boundary_name, boundary_deg = prev_name, prev_deg
        boundary_utc = (
            birth_dt_utc
            if (solar_lon - boundary_deg + 360.0) % 360.0 == 0.0
            else find_prev_longitude_time_utc(boundary_deg, birth_dt_utc)
        )
        delta_seconds = (birth_dt_utc - boundary_utc).total_seconds()

    start_age_years = delta_seconds / (72.0 * 3600.0)  # 三天一岁
    start_dt_utc = birth_dt_utc + timedelta(days=start_age_years * TROPICAL_YEAR_DAYS)

    month_idx = ganzhi_index_60(month_gz)
    step = 1 if forward else -1

    items = []
    for i in range(1, count + 1):
        idx = (month_idx + step * i) % 60
        gz = Ganzhi(idx % 10, idx % 12)
        age_years = start_age_years + (i - 1) * 10.0
        dt_utc = birth_dt_utc + timedelta(days=age_years * TROPICAL_YEAR_DAYS)
        items.append(
            {
                "i": i,
                "ganzhi": gz.text(),
                "age_years": round(age_years, 6),
                "start_time_local": dt_utc.astimezone(birth_dt_local.tzinfo).isoformat(timespec="seconds"),
            }
        )

    return {
        "direction": "顺" if forward else "逆",
        "boundary": {"name": boundary_name, "target_longitude_deg": boundary_deg},
        "boundary_time_local": boundary_utc.astimezone(birth_dt_local.tzinfo).isoformat(timespec="seconds"),
        "start_age_years": round(start_age_years, 6),
        "start_time_local": start_dt_utc.astimezone(birth_dt_local.tzinfo).isoformat(timespec="seconds"),
        "dayun": items,
    }


# ============================================================
# CLI
# ============================================================


def parse_birth_local(s: str, tz: ZoneInfo) -> datetime:
    # 允许 "YYYY-MM-DD HH:MM" / "YYYY-MM-DDTHH:MM" / 带秒
    s = s.strip().replace("T", " ")
    fmts = ["%Y-%m-%d %H:%M:%S", "%Y-%m-%d %H:%M"]
    last_err = None
    for fmt in fmts:
        try:
            dt = datetime.strptime(s, fmt)
            return dt.replace(tzinfo=tz)
        except ValueError as e:
            last_err = e
    raise ValueError(f"无法解析 birth='{s}': {last_err}")


def main() -> int:
    parser = argparse.ArgumentParser(description="八字排盘（立春换年 / 定气节气 / 真太阳时 / 子初换日）")
    parser.add_argument("--birth", required=True, help="出生时间（当地标准时），如 '1991-07-15 13:20'")
    parser.add_argument("--tz", required=True, help="IANA 时区名，如 'Asia/Shanghai'")
    parser.add_argument("--lat", type=float, required=True, help="纬度（度），北纬为正（当前版本不参与计算，但作为定位必须输入）")
    parser.add_argument("--lon", type=float, required=True, help="经度（度），东经为正")
    parser.add_argument("--sex", required=True, help="性别：M/F/男/女（用于大运顺逆）")
    parser.add_argument("--dayun", type=int, default=10, help="输出多少步大运（默认 10）")
    args = parser.parse_args()

    tz = ZoneInfo(args.tz)
    birth_local = parse_birth_local(args.birth, tz)
    birth_utc = birth_local.astimezone(timezone.utc)

    # ---- 年柱（立春换年）----
    year_for_gz, lichun_utc = year_ganzhi_by_lichun(birth_utc, birth_local.year)
    y_gz = year_ganzhi(year_for_gz)

    # ---- 月柱（按节切分）----
    solar_lon = sun_apparent_longitude_deg(julian_day(birth_utc))
    m_gz = month_ganzhi(y_gz.stem_index, solar_lon)

    # ---- 真太阳时（用于时柱与子初换日）----
    solar = true_solar_label(birth_local, args.lon)
    eff_date = solar.solar_date + (timedelta(days=1) if solar.solar_minutes >= 1380.0 else timedelta(days=0))

    # ---- 日柱 / 时柱 ----
    d_gz = day_ganzhi_by_effective_date(eff_date)
    h_gz = hour_ganzhi(d_gz.stem_index, solar.solar_minutes)

    # ---- 大运 ----
    dayun = compute_dayun(
        birth_dt_local=birth_local,
        birth_dt_utc=birth_utc,
        sex=args.sex,
        lon_deg=args.lon,
        year_gz=y_gz,
        month_gz=m_gz,
        count=max(1, int(args.dayun)),
    )

    # ============================================================
    # 输出（保持“人能读懂”的格式；需要机器读可后续加 --json）
    # ============================================================
    print("============================================================")
    print("八字排盘（立春换年 / 定气节气 / 真太阳时 / 子初换日）")
    print("------------------------------------------------------------")
    print(f"Birth(local) : {birth_local.isoformat(timespec='seconds')}")
    print(f"Birth(UTC)   : {birth_utc.isoformat(timespec='seconds')}")
    print(f"Location     : lat={args.lat:.6f}, lon={args.lon:.6f}")
    print("------------------------------------------------------------")
    print(f"SunLon(deg)  : {solar_lon:.6f}")
    print(f"EoT(min)    : {solar.equation_of_time_minutes:+.3f}")
    print(f"TST(label)   : {solar.solar_date.isoformat()} {solar.hhmmss()}  (true solar time)")
    print(f"EffDay(date) : {eff_date.isoformat()}  (>=23:00 -> next day)")
    print("------------------------------------------------------------")
    print(f"Lichun(UTC)  : {lichun_utc.isoformat(timespec='seconds')}")
    print(f"Year(for GZ) : {year_for_gz} -> {y_gz.text()}")
    print(f"Month        : {m_gz.text()}")
    print(f"Day          : {d_gz.text()}")
    print(f"Hour         : {h_gz.text()}")
    print("------------------------------------------------------------")
    print(f"DaYun dir    : {dayun['direction']}")
    print(
        f"QiYun        : boundary={dayun['boundary']['name']} (lon={dayun['boundary']['target_longitude_deg']:.0f}°), "
        f"boundary_time={dayun['boundary_time_local']}, start_age={dayun['start_age_years']}y"
    )
    print("------------------------------------------------------------")
    for item in dayun["dayun"]:
        print(f"DaYun {item['i']:02d}: {item['ganzhi']}  age={item['age_years']:.6f}  start={item['start_time_local']}")
    print("============================================================")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())



"""从解包的游戏数据（gamedata/json2、json3）生成内置的条件文字和「决斗输了会死」的关卡表（conditions.json.gz）。

游戏的条件表只挂在剧情场景上，读档后直接在养成地图打开剧情回顾时拿不到；Mod 先用这份内置的，
进过一次剧情场景后改用游戏里现读的（ConditionBook）。游戏更新后重新解包、重跑这个脚本。
用法：python tools/make-conditions.py <gamedata 目录>
"""
import gzip
import json
import os
import sys

gamedata = os.path.abspath(sys.argv[1])
sys.path.insert(0, gamedata)
sys.argv = ["x"]
from ann import CONDS, SWITCH, cond_item  # noqa: E402

# 和 Mod 里的写法一致：「名字 需>=35」，多项之间「且 / 或」
switches = {}
for name, s in SWITCH.items():
    if s.get("_useCount"):
        continue
    switches[name] = [cond_item(ci).replace(" ≥ ", " 需>=").replace(" ≤ ", " 需<=").replace(" > ", " 需>").replace(" < ", " 需<")
                      .replace(" ≠ ", " 需!=").replace(" = ", " 需=") for ci in s.get("_items", [])]
conditions = {}
for name, c in CONDS.items():
    items = [cond_item(ci) for ci in c.get("_items", [])]
    conditions[name] = " 或 ".join(items)

# 决斗输了会死的关卡：关卡名去掉 CL_ 前缀就是剧本里 ChangeScene("Combat", "...") 的那个名字
combat_deaths = {}
levels = os.path.join(gamedata, "json3", "sharedassets6__CombatLevel.json")
if os.path.exists(levels):
    for level in json.load(open(levels, encoding="utf-8")):
        dead = (level.get("_deadLibrary") or "").lstrip("@")
        if level.get("_deadEnd") and dead:
            combat_deaths[level["m_Name"][3:] if level["m_Name"].startswith("CL_") else level["m_Name"]] = dead

# 决斗关卡的说明和回合条件（给 AI 看）：「第 8 回合 → 瑞笙加入我方」「第 15 回合 → 战斗结束」这类。
# 条件、动作在解包数据里是资产名（回合数_EQ_8、我方_新增_C506……），这里换成看得懂的写法；台词不要
import glob
import re
from ann import t2s  # noqa: E402

assets = {}
for f in glob.glob(os.path.join(gamedata, "json3", "sharedassets6__*.json")):
    for x in json.load(open(f, encoding="utf-8")):
        if "m_Name" in x:
            assets[x["m_Name"]] = x

OPS = {"EQ": "=", "GE": "≥", "LE": "≤", "GT": ">", "LT": "<", "NE": "≠"}

# 旗标的开发备注（M50044「唤醒小梅」这类），给「计数」补上名字
flag_notes = {}
flags_file = os.path.join(gamedata, "json3", "globalgamemanagers__FlagData.json")
if os.path.exists(flags_file):
    for x in json.load(open(flags_file, encoding="utf-8")):
        # 「决斗泛用事件旗标」「旗标暂存」这类通用旗标的备注不说明什么，不补
        if x.get("m_Name") and x.get("_devNote") and not re.search("泛用|暫存|暂存", x["_devNote"]):
            flag_notes[x["m_Name"]] = t2s(x["_devNote"]).strip()


def cond_text(name):
    name = t2s((name or "").lstrip("@"))
    m = re.fullmatch(r"回合数_(EQ|GE|LE|GT|LT)_(\d+)", name)
    if m:
        return ("第 %s 回合" % m.group(2)) if m.group(1) == "EQ" else ("回合数 %s %s" % (OPS[m.group(1)], m.group(2)))
    m = re.fullmatch(r"(我方|敌方|敌人)_(\w+?)_(LE|GE|LT|GT)_PCT_(\d+)", name)
    if m:
        return "%s%s %s %s%%" % (m.group(1), m.group(2), OPS[m.group(3)], m.group(4))
    m = re.fullmatch(r"旗标_(\w+?)_(EQ|GE|LE|GT|LT|NE)_(-?\d+)", name)
    if m:
        note = flag_notes.get(m.group(1), "")
        return "计数 %s%s %s %s" % (m.group(1), "「%s」" % note if note else "", OPS[m.group(2)], m.group(3))
    if name.startswith("旗标回合_"):
        return "到了约定的回合"
    return t2s(name).replace("_", " ")


def action_text(name):
    if not name:
        return None
    x = assets.get(name.lstrip("@"), {})
    name = t2s(name.lstrip("@"))
    if "_dialogs" in x:
        return None
    effect = (x.get("_effect") or "").lstrip("@") if isinstance(x.get("_effect"), str) else ""
    m = re.fullmatch(r"(我方|敌方)_(新增|移除)_\w+", name)
    if m and effect:
        who = re.sub(r"^[A-Z]\d+_?", "", effect) or effect
        text = "%s%s「%s」" % (m.group(1), "加入" if m.group(2) == "新增" else "移除", t2s(who))
        # C_Effect_103「依序嘴攻台詞」：带这个状态的人每嘴攻一次，换下一句台词，并把记录旗标（这场的计数）+1
        units = [u.lstrip("@") for u in assets.get(effect, {}).get("_units", []) if isinstance(u, str)]
        if m.group(2) == "新增" and any(u.startswith("C_Effect_103") for u in units):
            text += "（每用一次嘴攻，「%s」的计数 +1）" % t2s(who)
        return text
    if name == "读取下一个场景":
        return "战斗结束"
    m = re.fullmatch(r"旗标_C00000_(\d)", name)
    if m:
        return "判赢" if m.group(1) == "1" else "判输"
    if re.fullmatch(r"旗标_[A-Z]+\d*_-?\d+", name):
        return None
    return t2s(name).replace("_", " ")


combats = {}
if os.path.exists(levels):
    for level in json.load(open(levels, encoding="utf-8")):
        key = level["m_Name"][3:] if level["m_Name"].startswith("CL_") else level["m_Name"]
        lines = [t2s(level.get("_desc", "")).strip() + ("（输了就是死亡结局）" if level.get("_deadEnd") else "")]
        for e in level.get("Events", []):
            event = assets.get(e.lstrip("@"), {})
            start = [s for s in (action_text(a) for a in event.get("_initAction", [])) if s]
            if start:
                lines.append("开场：" + "、".join(start))
            for ca in event.get("_conditionAction", []):
                c = assets.get(ca.lstrip("@"), {})
                acts = [s for s in (action_text(a) for a in c.get("_actions", []) + c.get("_afterActions", [])) if s]
                if not acts:
                    continue
                join = " 且 " if c.get("_logic", 0) == 0 else " 或 "
                lines.append(join.join(cond_text(x) for x in c.get("_conditions", [])) + " → " + "、".join(acts))
        combats[key] = lines

book = {"Version": "builtin", "Switches": switches, "Conditions": conditions, "CombatDeaths": combat_deaths, "Combats": combats}
out = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "LOMAssistant", "conditions.json.gz")
with gzip.open(out, "wt", encoding="utf-8") as f:
    json.dump(book, f, ensure_ascii=False)
print(f"switches {len(switches)}, conditions {len(conditions)}, combat deaths {len(combat_deaths)}, combats {len(combats)}")

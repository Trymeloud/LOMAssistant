using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace LOMAssistant.Features.Ai;

internal static class KoreanAiTools
{
    internal static (string Name, string Description, JObject Parameters)[] Localize((string Name, string Description, JObject Parameters)[] original)
    {
        var result = new (string Name, string Description, JObject Parameters)[original.Length];
        for (int i = 0; i < original.Length; i++)
        {
            var tool = original[i];
            var schema = (JObject)tool.Parameters.DeepClone();
            Rewrite(schema, tool.Name, "");
            result[i] = (tool.Name, Descriptions[tool.Name], schema);
        }
        return result;
    }

    private static void Rewrite(JToken token, string tool, string path)
    {
        if (token is JObject obj)
        {
            foreach (var property in obj.Properties())
            {
                if (property.Name == "description") property.Value = Parameters[tool + path];
                else Rewrite(property.Value, tool, path + "/" + property.Name);
            }
        }
        else if (token is JArray array)
            for (int i = 0; i < array.Count; i++) Rewrite(array[i], tool, path + "/" + i);
    }

    private static readonly Dictionary<string, string> Descriptions = new Dictionary<string, string>
    {
        ["search"] = "인물명·임무명·설명·요약·대사·결말·업적·인연 대상 인물을 검색합니다. ID를 모르면 먼저 사용하세요. 일치하는 임무·스크립트·결말 ID와 발췌를 반환합니다.",
        ["quest"] = "임무 그룹·주요/곁가지 구분·시기·장소·발생 조건·진입 스크립트·이후 임무·포함 스크립트와 기록을 조회합니다. 모든 회차 대사 읽은 비율과 이번 회차 방문 여부를 구분합니다.",
        ["script"] = "스크립트의 스토리 기록·선택지·분기별 수치와 플래그 변화·연결 대상·결말·사망·결투·대사 원문을 조회합니다. 선택지와 연결마다 모든 회차 읽은 비율과 이번 회차 방문 여부를 표시합니다. 긴 대사는 페이지를 나눕니다.",
        ["endings"] = "한청서(결말)·생사부(사망)·업적·결말 CG의 이름·설명·달성 스크립트와 분기를 검색합니다. 도감 번호로도 찾습니다. 보상·회차·운명·새 게임 검색은 도감 수집과 업적에 따른 새 게임+ 보상을 함께 표시합니다. 빈 검색어면 전체 이름을 나열합니다.",
        ["events"] = "장소의 일·수련·산책 중 추첨하는 이벤트를 찾습니다. 장소·이름·추첨 가중치·가중치 증감 조건·가능한 결과·스크립트 ID를 반환합니다. 분기 상세는 script로 확인하세요. 임무 외 이벤트를 찾을 때 대사 검색보다 먼저 사용하세요.",
        ["item"] = "물품·서적·잡화·스토리 물품·소모품의 설명·이번 회차 수량·사용 효과·이야기의 획득/판정/회수 지점·판매처·개발 완료 보상을 조회합니다. 장비·암기·함정·방어구·단약·독 개발의 문파 규모 조건·비용·레벨별 점수·현재 레벨·선행 프로젝트와 파생도 확인합니다. 비급의 유형·문파·필요 점수·수련 조건·성취 효과·매번 소모량·현재 레벨, 재능의 설명·최대 레벨·획득 비급과 프로젝트도 조회합니다.",
        ["shop"] = "사사형 상점의 현재 재고·원가·남은 수량·잡화와 소모품의 효과, 이후 월별 입고 물품을 조회합니다. 판매 물품·구매 효율·판매 시기를 물을 때 사용하세요.",
        ["sect"] = "문파 규모·자산·명성·인원·구심력과 시설별 현재 레벨·매 순 수입·레벨별 수치 변화·추가 효과에 필요한 플래그를 조회합니다. 자산의 증감·규모 상승·시설 용도를 확인할 때 사용하세요.",
        ["person"] = "게임 인물지의 칭호·출신·문파·친족·성격을 조회합니다. 인물의 정체나 가족·스승을 물으면 대사 전체 검색 대신 먼저 한 번 사용하세요. 스포일러 설정에 따라 숨겨진 과거와 진상은 제한됩니다.",
        ["heroine"] = "인연 대상 인물의 경로를 조회합니다. 필요한 임무·각 단계 호감도 기준·인연 플래그와 현재 진행도를 표시합니다. 마음에 둔 사람과 인연 성립을 구분하세요.",
        ["usage"] = "플래그 이름/ID, 인물의 호감도, 수치·기술·재능을 역검색합니다. 어느 스크립트·임무·분기에서 변경/판정하는지와 변경값·요구값을 조회하며 플래그는 이번 회차 현재 값도 표시합니다. 호감도 증감의 정확한 경로 합계는 routes를 쓰세요.",
        ["routes"] = "임무 시작 또는 지정 스크립트에서 끝까지 이어지는 각 경로의 선택·주사위·조건과 호감도/수치 합계·플래그 설정·결말을 계산합니다. 서로 배타적인 분기를 직접 더하지 마세요. 최댓값·최적 경로 질문은 반드시 이 도구를 사용하세요.",
        ["settings"] = "모드 설정의 이름·현재 값·선택값/범위·설명·재시작 필요 여부를 조회합니다. 변경은 이후 propose_change로 제안하세요. AI 접속 설정은 변경 대상에서 제외됩니다.",
        ["player_state"] = "현재 날짜·주요 임무·발생 가능한 곁가지 임무·진행 중 이야기·화면 선택지/주사위·주요 수치·호감도·인연 상태를 조회합니다. 이번 진행도 관련 답변 전에 사용하세요. 플래그는 포함하지 않으므로 사문·진행 카운터·발생 여부는 usage에서 현재 값을 조회하세요.",
        ["propose_change"] = "수치 변경 카드를 제안합니다. 현재 값과 새 값이 표시되며 사용자가 확인해야 적용됩니다. 거절하면 적용하지 않습니다. 수치 화면의 수치·호감도(호감도·인물명)·마음에 둔 사람·재능·플래그(ID 또는 플래그·이름, 보통 완료 1/미완료 0)·서적/물품 수량을 바꿀 수 있습니다. 모드 설정은 설정·항목명으로 쓰고 먼저 settings에서 정확한 이름과 선택값을 확인하세요. 켜기/끄기와 선택지 원문을 받습니다. 호출 후 확인을 요청하고 이미 변경했다고 말하지 마세요. 체력·심상·성격·학식·호감도 등은 대체로 0~100이며 상한 초과를 명시하지 않은 '최대'는 100입니다. 마음에 둔 사람은 0~8 ID 또는 이름입니다.",
    };
    private static readonly Dictionary<string, string> Parameters = new Dictionary<string, string>
    {
        ["search/properties/query"] = "공식 한국어 인물명·임무명·장소명·대사의 단어",
        ["quest/properties/key"] = "search에서 확인한 임무 ID(예: S2700_01)",
        ["script/properties/name"] = "스크립트 ID(예: S2700_02_01)",
        ["script/properties/page"] = "대사 페이지. 1부터 시작하며 생략 시 1",
        ["endings/properties/query"] = "검색어 또는 도감 번호. 빈 문자열이면 전체 이름",
        ["events/properties/query"] = "이벤트명·장소명 또는 관련 대사의 단어",
        ["item/properties/name"] = "공식 한국어 물품·개발 프로젝트·비급·재능 이름",
        ["person/properties/name"] = "인물명 또는 호칭(예: 소사매·우소매·이사형)",
        ["heroine/properties/name"] = "인연 대상 인물명 또는 호칭(예: 소사매·욱죽)",
        ["usage/properties/name"] = "플래그 이름/ID(예: G11006), 인물명, 수치·기술·재능 이름",
        ["routes/properties/key"] = "임무 ID(예: S2700_01) 또는 스크립트 ID",
        ["routes/properties/focus"] = "중점 수치(예: 욱죽 호감도·심상). 내림차순 정렬과 최대/최소를 표시하며 관심 항목이 없으면 빈 문자열",
        ["settings/properties/query"] = "설정 검색어(예: 배속·주사위·수시 저장). 빈 문자열이면 전체",
        ["propose_change/properties/changes"] = "변경할 항목 목록",
        ["propose_change/properties/changes/items/properties/name"] = "수치 화면의 이름(예: 심상·은전·호감도·우소매·마음에 둔 사람) 또는 설정·항목명",
        ["propose_change/properties/changes/items/properties/value"] = "새 정수 값. 마음에 둔 사람은 인물 이름도 가능하며 설정은 선택값 원문도 가능",
        ["propose_change/properties/reason"] = "카드에 표시할 한국어 변경 사유 한 문장",
    };
}

namespace LOMAssistant.Features.Ai;

// Select text at its source; never translate tool JSON keys or calculation data.
internal static class AiText
{
    internal static string T(string original, string korean) => NativeKorean.Enabled ? korean : original;
    internal static string Label(string text) => NativeKorean.Enabled
        ? UiText.Translate(KoreanLabels.Variable(text), UiText.Korean) : text;
    internal static string Provider(string name) => NativeKorean.Enabled ? name switch
    {
        "Kimi（月之暗面）" => "Kimi (Moonshot)",
        "通义千问（阿里云百炼）" => "Qwen (Alibaba Cloud)",
        "智谱 GLM" => "GLM (Zhipu)", "硅基流动" => "SiliconFlow",
        "Claude（Anthropic）" => "Claude (Anthropic)", "Gemini（Google）" => "Gemini (Google)",
        "本地模型（Ollama）" => "로컬 모델 (Ollama)", "自定义（OpenAI 兼容）" => "직접 설정 (OpenAI 호환)",
        _ => name,
    } : name;
}

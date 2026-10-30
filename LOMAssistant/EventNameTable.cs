using System;
using System.Collections.Generic;

namespace LOMAssistant;

// Event IDs such as Travel_F_01 are reused with different labels in other pools.
internal sealed class EventNameTable
{
    private readonly Dictionary<(string Id, string Original), string> translations = new Dictionary<(string, string), string>();

    internal void Add(string id, string original, string korean)
    {
        translations[(id, original.Trim())] = korean;
    }

    internal bool TryGet(string id, string original, out string korean)
    {
        korean = null;
        return !string.IsNullOrEmpty(id) && original != null
            && translations.TryGetValue((id, original.Trim()), out korean);
    }
}

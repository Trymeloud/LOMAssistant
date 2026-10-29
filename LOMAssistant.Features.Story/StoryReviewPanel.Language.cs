namespace LOMAssistant.Features.Story;

public partial class StoryReviewPanel
{
    private string _storyDisplayLanguage;
    private void RefreshStoryLanguage()
    {
        if (_storyDisplayLanguage == TextConverter.DisplayLanguage) return;
        _storyDisplayLanguage = TextConverter.DisplayLanguage;
        _texts.Clear();
        _reading.Clear();
        _flowCache.Clear();
        _flowCacheVersion = -1;
        _flow = null;
        _route = null;
        _searchedFor = _flagSearchedFor = null;
        StoryAtlas.ForgetConditions();
    }
}

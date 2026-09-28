# ACCORDION & TAB STATE PERSISTENCE STANDARD

## 1. Objective
To provide a seamless, non-intrusive, and resilient UX across all Blazor Interactive Server pages where Accordions and Tabs are used, ensuring that browser refresh (F5) or navigation returns always restore the user's active Tab and expanded Accordion item without breaking the Blazor Circuit.

## 2. Core Implementation Rules

### Rule 1: Supply Parameter From Query
Always declare query parameters for state restoration:
```csharp
[SupplyParameterFromQuery(Name = "tab")] public string? SecureTab { get; set; }
[SupplyParameterFromQuery(Name = "sac")] public string? SecureSac { get; set; }
```

### Rule 2: Explicit Selection Binding & Per-Tab State Storage
Use a Dictionary to remember the active accordion item per tab independently:
```csharp
private readonly Dictionary<int, int> _tabAccordionIndices = new() { [0] = 0, [1] = 0, [2] = 0, [3] = 0 };

private int GetAccordionIndexForTab(int tab) => _tabAccordionIndices.TryGetValue(tab, out var idx) ? idx : 0;
private void SetAccordionIndexForTab(int tab, int index) => _tabAccordionIndices[tab] = Math.Max(0, index);
```

Bind `Selected` property on `RadzenAccordionItem` to ensure immediate expansion on render:
```razor
<RadzenAccordionItem Text="@($"{group.Code} - {group.Title}")" Icon="folder"
                     Selected="@(currentGroupIdx == GetAccordionIndexForTab(_activeTabIndex))"
                     Class="@(hasTopics ? "acc-has-topics shadow-sm mb-2" : "shadow-sm mb-2")">
```

### Rule 3: Silent URL Synchronization via history.replaceState
Never call `NavigationManager.NavigateTo` inside rendering loops or UI interaction handlers. Always update the browser address bar silently using JS Interop:
```csharp
protected override async Task OnAfterRenderAsync(bool firstRender)
{
    if (firstRender)
    {
        await SyncBrowserUrlAsync();
    }
}

private async Task SyncBrowserUrlAsync()
{
    try
    {
        var curAcc = GetAccordionIndexForTab(_activeTabIndex);
        var queryParams = new Dictionary<string, object?>
        {
            ["tab"] = _activeTabIndex.ToString(),
            ["sac"] = curAcc >= 0 ? curAcc.ToString() : null,
            ["q"] = !string.IsNullOrWhiteSpace(_searchText) ? _searchText : null
        };

        var uri = NavigationManager.GetUriWithQueryParameters(NavigationManager.ToBaseRelativePath(NavigationManager.Uri).Split('?')[0], queryParams);
        await JSRuntime.InvokeVoidAsync("history.replaceState", null, "", uri);
    }
    catch
    {
        // Ignored during prerender or disconnect
    }
}
```

### Rule 4: Per-Tab Accordion Independence on Tab Change
When user switches a tab, do NOT reset other tabs' remembered accordion indices. Preserve each tab's memory and synchronize the URL:
```csharp
private async Task OnTabChanged(int index)
{
    _activeTabIndex = index;
    await SyncBrowserUrlAsync();
}

private async Task OnAccordionScopeChanged(eTgoScopeNo scope, int index)
{
    var tab = ScopeToTabIndex(scope);
    SetAccordionIndexForTab(tab, index);
    await SyncBrowserUrlAsync();
}
```

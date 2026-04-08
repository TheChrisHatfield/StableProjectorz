using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace spz {

	// Controls a list of tags, which user can click.
	// We will disable other tags.
	// Disables dividers of the tabs, when they are next to a clicked tab.
	// Dividers are a separation-line between inactive tabs.
	public class TabsGroup_UI : MonoBehaviour{
	    [Space(10)]
	    [SerializeField] string _default_activeTab = "";//if non "", we'll activate it.
	    [SerializeField] List<TabsGroupElem_UI> _tabs = new List<TabsGroupElem_UI>();
	    bool _tabsSwitched_atLeastOnce = false;

	    /// <returns>False if no tab matched <paramref name="tabName"/>; listener was not added.</returns>
	    public bool SubscribeForTab(string tabName, Action<TabsGroupElem_UI> act){
	        if (_tabs == null) _tabs = new List<TabsGroupElem_UI>();
	        string nameLower = tabName.ToLower();
	        var tab = _tabs.FirstOrDefault(t=>t != null && t.title != null && t.title.ToLower()==nameLower);
	        if(tab == null){ 
	            UnityEngine.Debug.LogWarning($"[TabsGroup_UI] SubscribeForTab: Tab '{tabName}' not found in list of {_tabs.Count} tabs");
	            return false; 
	        }
	        tab.onClicked += act;
	        return true;
	    }

	    /// <summary>Add a tab at runtime (e.g. Addons). Ribbon will adjust if it has a flexible layout.</summary>
	    public void AddTab(TabsGroupElem_UI tabElem){
	        if(tabElem == null) return;
	        if (_tabs == null) _tabs = new List<TabsGroupElem_UI>();
	        _tabs.Add(tabElem);
	        tabElem.onClicked += OnTabClicked;
	        ReorderTabsByStripSiblingIndex();
	        UnityEngine.Debug.Log($"[TabsGroup_UI] Added runtime tab: {tabElem.title}. Total tabs: {_tabs.Count}");
	    }

	    /// <summary>Remove a tab from the group (e.g. when an addon is disabled). Call before destroying the tab GameObject.</summary>
	    public void RemoveTab(TabsGroupElem_UI tabElem){
	        if(tabElem == null || _tabs == null) return;
	        _tabs.Remove(tabElem);
	        ReorderTabsByStripSiblingIndex();
	    }

	    /// <summary>
	    /// Keep <see cref="_tabs"/> order aligned with tab-button sibling order on the strip so <see cref="OnTabClicked"/>
	    /// divider logic matches visual order (runtime tabs appended with <c>SetAsLastSibling</c> must not sit at wrong list indices).
	    /// </summary>
	    void ReorderTabsByStripSiblingIndex(){
	        if (_tabs == null || _tabs.Count <= 1) return;
	        Transform strip = GetTabStripTransform();
	        if (strip == null) return;
	        _tabs.Sort((a, b) => {
	            if (a == null && b == null) return 0;
	            if (a == null) return 1;
	            if (b == null) return -1;
	            Transform pa = a.transform.parent;
	            Transform pb = b.transform.parent;
	            if (pa != strip) return pb == strip ? 1 : string.Compare(a.title, b.title, StringComparison.OrdinalIgnoreCase);
	            if (pb != strip) return -1;
	            return a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex());
	        });
	    }

	    /// <summary>Transform under which tab buttons live (for adding runtime tabs so they appear in the ribbon).</summary>
	    public Transform GetTabStripTransform(){
	        if (_tabs != null && _tabs.Count > 0 && _tabs[0] != null)
	            return _tabs[0].transform.parent;
	        return transform;
	    }

	    /// <summary>True if the group already has a tab with the given title (case-insensitive).</summary>
	    public bool HasTab(string tabTitle){
	        if (_tabs == null || string.IsNullOrEmpty(tabTitle)) return false;
	        string nameLower = tabTitle.ToLower();
	        return _tabs.Any(t => t != null && t.title != null && t.title.ToLower() == nameLower);
	    }

	    public void SwitchTab(string tabName){
	        _tabsSwitched_atLeastOnce = true;
	        if (_tabs == null) return;
	        string nameLower = tabName.ToLower();
	        var tab = _tabs.FirstOrDefault(t=> t != null && t.title != null && t.title.ToLower()==nameLower);
	        if(tab == null){ return; }
	        tab.Toggle(true);
	    }

	    /// <summary>Returns false if no tab matches (same matching rules as <see cref="SwitchTab"/>).</summary>
	    public bool TrySwitchTab(string tabName){
		    if (_tabs == null || string.IsNullOrEmpty(tabName)) return false;
		    string nameLower = tabName.ToLower();
		    var tab = _tabs.FirstOrDefault(t => t != null && t.title != null && t.title.ToLower() == nameLower);
		    if (tab == null) return false;
		    SwitchTab(tabName);
		    return true;
	    }

	    /// <summary>Visible tab titles in list order (built-in + runtime add-on tabs).</summary>
	    public List<string> GetTabTitles(){
		    var r = new List<string>();
		    if (_tabs == null) return r;
		    foreach (var t in _tabs) {
			    if (t != null && !string.IsNullOrEmpty(t.title))
				    r.Add(t.title);
		    }
		    return r;
	    }

	    void OnTabClicked(TabsGroupElem_UI elem){
	        if (_tabs == null) return;
	        int ixOfClicked = -1;
	        for(int i=0; i<_tabs.Count; i++){
	            if(_tabs[i] == null) continue;
	            if(_tabs[i] == elem){ 
	                ixOfClicked = i;
	                elem.Toggle(true);
	                continue; 
	            }
	            _tabs[i].Toggle(false);
	        }//end for

	        if (ixOfClicked < 0) return;

	        //make sure the neighboring tabs have their adjacent divider-lines hidden (neighbors may be null if _tabs has holes from RemoveTab):
	        if(ixOfClicked > 0 && _tabs[ixOfClicked - 1] != null){
	            _tabs[ixOfClicked - 1].DisableDivider(isLeft:false);
	        }
	        if(ixOfClicked < _tabs.Count - 1 && _tabs[ixOfClicked + 1] != null){
	            _tabs[ixOfClicked + 1].DisableDivider(isLeft:true);
	        }
	    }


	    void Awake(){
	        if (_tabs == null) return;
	        for (int i=0; i<_tabs.Count; ++i){
	            if (_tabs[i] != null)
	                _tabs[i].onClicked += OnTabClicked;
	        }
	        ReorderTabsByStripSiblingIndex();
	    }


	    void Update(){
	        if (!_tabsSwitched_atLeastOnce && !string.IsNullOrEmpty(_default_activeTab)){
	            SwitchTab(_default_activeTab);
	        }
	    }
	}
}//end namespace

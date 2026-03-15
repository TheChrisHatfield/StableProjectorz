using System.Collections;
using System.Collections.Generic;
using System;
using UnityEngine;
using UnityEngine.UI;

namespace spz {

	//belongs to TabsGroup_UI, sits inside it together with other tab-elements.
	public class TabsGroupElem_UI : MonoBehaviour{
	    [SerializeField] Button _button;
	    [SerializeField] GameObject _go_active;
	    [SerializeField] GameObject _go_inactive;
	    [SerializeField] GameObject _dividerLeft;// Dividers are a separation-line between inactive tabs.
	    [SerializeField] GameObject _dividerRight;

	    [SerializeField] string _title;
	    string _runtimeTitle; // set via InitForRuntime so runtime-created tabs have a title
	    public string title => !string.IsNullOrEmpty(_runtimeTitle) ? _runtimeTitle : _title;

	    bool _isInvoking_onClicked = false;//prevents recursive stack overflow
	    public Action<TabsGroupElem_UI> onClicked { get; set; }



	    //only invoked by our group, not from here.
	    public void Toggle(bool isOn){
	        if(_isInvoking_onClicked){ return; }//our own callback, avoid recursion

	        if(_go_active!=null){  _go_active.SetActive(isOn); }
	        if(_go_inactive!=null){ _go_inactive.SetActive(!isOn); }
	        if(_dividerLeft!=null){ _dividerLeft.SetActive(!isOn); }
	        if(_dividerRight!=null){ _dividerRight.SetActive(!isOn); }

	        if (isOn) { 
	            _isInvoking_onClicked = true;
	            onClicked?.Invoke(this);
	            _isInvoking_onClicked = false;
	        }
	    }


	    public void DisableDivider(bool isLeft){
	        if(isLeft){
	            if(_dividerLeft!=null){ _dividerLeft?.SetActive(false); }
	        }else{
	            if(_dividerRight!=null){ _dividerRight?.SetActive(false); }
	        }
	    }

	    /// <summary>Init a tab created at runtime (title and button ref).</summary>
	    public void InitForRuntime(string tabTitle, Button btn){
	        _runtimeTitle = tabTitle;
	        _button = btn;
	    }

	    /// <summary>Set the active-state highlight for a runtime-created tab (so it matches prefab tabs: selected = show this child).</summary>
	    public void SetRuntimeActiveHighlight(GameObject goActive){
	        _go_active = goActive;
	    }

	    void OnClicked() => Toggle(true);

	    void Start(){
	        if(_button != null) _button.onClick.AddListener( OnClicked );
	    }
	}
}//end namespace

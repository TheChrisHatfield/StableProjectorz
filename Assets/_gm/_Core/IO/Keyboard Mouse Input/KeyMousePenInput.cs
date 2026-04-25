using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using TMPro;
using System.Reflection;

namespace spz {

	public static class KeyMousePenInput{

	    // After stylus lift, keep logical cursor at last pen/hover position until the user moves or clicks the mouse
	    // (otherwise Windows leaves the mouse pointer elsewhere and the brush preview jumps away from the stroke).
	    static Vector2 _lastPenOrHoverScreenPos;
	    static bool _haveLastPenOrHoverScreenPos;
	    const float MouseDeltaToBreakPenLatchSq = 2.25f; // 1.5 px — ignore sub-pixel jitter

	    static System.Type _fileBrowserType;
	    static PropertyInfo _fileBrowserIsOpenProp;
	    static bool _fileBrowserReflectionTried = false;

	    public static bool isFileBrowserOpen(){
	        if (!_fileBrowserReflectionTried){
	            _fileBrowserReflectionTried = true;
	            _fileBrowserType = System.Type.GetType("SimpleFileBrowser.FileBrowser, SimpleFileBrowser.Runtime");
	            _fileBrowserIsOpenProp = _fileBrowserType?.GetProperty("IsOpen", BindingFlags.Public | BindingFlags.Static);
	        }

	        if (_fileBrowserIsOpenProp == null){ return false; }
	        object value = _fileBrowserIsOpenProp.GetValue(null, null);
	        return value is bool b && b;
	    }

	    static bool IsGlobalInputBlocked(){
	        return isFileBrowserOpen();
	    }

	    /// <summary>True when pen tip is pressed (and no barrel/eraser). Used for Wacom stylus: tip = brush mode.</summary>
	    public static bool isPenTipPressed(){
	        if (Pen.current == null) return false;
	        if (!Pen.current.tip.isPressed) return false;
	        if (Pen.current.firstBarrelButton.isPressed || Pen.current.secondBarrelButton.isPressed || Pen.current.thirdBarrelButton.isPressed || Pen.current.fourthBarrelButton.isPressed || Pen.current.eraser.isPressed) return false;
	        return true;
	    }
	    /// <summary>True when pen eraser end is pressed. Used for Wacom stylus: eraser = erase mode.</summary>
	    public static bool isPenEraserPressed(){
	        return Pen.current != null && Pen.current.eraser.isPressed;
	    }
	    public static bool isPenTipPressedThisFrame(){
	        if (Pen.current == null) return false;
	        if (!Pen.current.tip.wasPressedThisFrame) return false;
	        if (Pen.current.firstBarrelButton.isPressed || Pen.current.secondBarrelButton.isPressed || Pen.current.thirdBarrelButton.isPressed || Pen.current.fourthBarrelButton.isPressed || Pen.current.eraser.isPressed) return false;
	        return true;
	    }
	    public static bool isPenEraserPressedThisFrame(){
	        return Pen.current != null && Pen.current.eraser.wasPressedThisFrame;
	    }

	    public static bool isLMBpressed(bool checkOnlyPen=false){
	        if (IsGlobalInputBlocked()) { return false; }
	        bool isMousePressed =  Mouse.current!=null  &&  Mouse.current.leftButton.isPressed;
	        bool isPenTip  =  Pen.current != null  &&  Pen.current.tip.isPressed;
	        if(isPenTip){
	            isPenTip &= Pen.current.firstBarrelButton.isPressed==false;
	            isPenTip &= Pen.current.secondBarrelButton.isPressed==false;
	            isPenTip &= Pen.current.thirdBarrelButton.isPressed==false;
	            isPenTip &= Pen.current.fourthBarrelButton.isPressed==false;
	            isPenTip &= Pen.current.eraser.isPressed==false;
	            if(!isPenTip){ isMousePressed=false; }
	        }
	        bool isPenEraser = isPenEraserPressed();
	        if(checkOnlyPen){ return isPenTip || isPenEraser; }
	        return isMousePressed || isPenTip || isPenEraser;
	    }

	    public static bool isLMBpressedThisFrame(){
	        if (IsGlobalInputBlocked()) { return false; }
	        bool isMousePressed = Mouse.current!=null  &&  Mouse.current.leftButton.wasPressedThisFrame;
	        bool isPenTip  =  Pen.current != null  &&  Pen.current.tip.wasPressedThisFrame;
	        if(isPenTip){
	            isPenTip &= Pen.current.firstBarrelButton.isPressed==false;
	            isPenTip &= Pen.current.secondBarrelButton.isPressed==false;
	            isPenTip &= Pen.current.thirdBarrelButton.isPressed==false;
	            isPenTip &= Pen.current.fourthBarrelButton.isPressed==false;
	            isPenTip &= Pen.current.eraser.isPressed==false;
	            if(!isPenTip){ isMousePressed=false; }
	        }
	        bool isPenEraser = isPenEraserPressedThisFrame();
	        return isMousePressed || isPenTip || isPenEraser;
	    }

	    public static bool isLMBreleasedThisFrame(){
	        if (IsGlobalInputBlocked()) { return false; }
	        bool isMouseReleased =  Mouse.current!=null  &&  Mouse.current.leftButton.wasReleasedThisFrame;
	        bool isPenTipReleased   =  Pen.current != null  &&  Pen.current.tip.wasReleasedThisFrame;
	        bool isPenEraserReleased = Pen.current != null && Pen.current.eraser.wasReleasedThisFrame;
	        return isMouseReleased || isPenTipReleased || isPenEraserReleased;
	    }


	    public static bool isRMBpressed(){
	        if (IsGlobalInputBlocked()) { return false; }
	        bool isMousePressed =  Mouse.current!=null  &&  Mouse.current.rightButton.isPressed;
	        bool isPenPressed  =  Pen.current != null  &&  Pen.current.firstBarrelButton.isPressed;
	        return isMousePressed || isPenPressed;
	    }

	    public static bool isRMBpressedThisFrame(){
	        if (IsGlobalInputBlocked()) { return false; }
	        bool isMousePressed =  Mouse.current!=null  &&  Mouse.current.rightButton.wasPressedThisFrame;
	        bool isPenPressed  =  Pen.current != null  &&  Pen.current.firstBarrelButton.wasPressedThisFrame;
	        return isMousePressed || isPenPressed;
	    }


	    public static bool isMMBpressed(){
	        if (IsGlobalInputBlocked()) { return false; }
	        bool isMousePressed =  Mouse.current!=null  &&  Mouse.current.middleButton.isPressed;
	        bool isPenPressed  =  Pen.current != null  &&  Pen.current.secondBarrelButton.isPressed;
	        return isMousePressed || isPenPressed;
	    }

	    public static bool isMMBpressedThisFrame(){
	        if (IsGlobalInputBlocked()) { return false; }
	        bool isMousePressed =  Mouse.current!=null  &&  Mouse.current.middleButton.wasPressedThisFrame;
	        bool isPenPressed  =  Pen.current != null  &&  Pen.current.secondBarrelButton.wasPressedThisFrame;
	        return isMousePressed || isPenPressed;
	    }

	    public static bool isMMBreleasedThisFrame(){
	        if (IsGlobalInputBlocked()) { return false; }
	        bool isMousePressed =  Mouse.current!=null  &&  Mouse.current.middleButton.wasReleasedThisFrame;
	        bool isPenPressed  =  Pen.current != null  &&  Pen.current.secondBarrelButton.wasReleasedThisFrame;
	        return isMousePressed || isPenPressed;
	    }


	    // NO NEED TO SCALE its output by Time.deltaTime
	    // See https://discussions.unity.com/t/mouse-sensitivity-changes-between-editor-and-built-exe/20038
	    public static Vector2 delta_cursor( bool normalizeByScreenDiagonal=true ){
	        if (IsGlobalInputBlocked()) { return Vector2.zero; }
	        float inv_screenDiagonal =  1.0f / Mathf.Sqrt(Screen.width*Screen.width + Screen.height*Screen.height);
	        Vector2 mouseDT =  Mouse.current!=null ?  Mouse.current.delta.ReadValue() : Vector2.zero;
	        Vector2 penDT   =  Pen.current != null ?  Pen.current.delta.ReadValue() : Vector2.zero;
	        mouseDT *= normalizeByScreenDiagonal? inv_screenDiagonal : 1;
	        penDT   *= normalizeByScreenDiagonal? inv_screenDiagonal : 1;
	        penDT.y *= -1; // Invert Y if necessary for tablet setup
	        return (mouseDT.sqrMagnitude > penDT.sqrMagnitude) ? mouseDT : penDT;
	    }


	    // NO NEED TO SCALE its output by Time.deltaTime
	    // See https://discussions.unity.com/t/mouse-sensitivity-changes-between-editor-and-built-exe/20038
	    public static Vector2 delta_while_LMBpressed( bool normalizeByScreenDiagonal=true ){
	        if (IsGlobalInputBlocked()) { return Vector2.zero; }
	        float inv_screenDiagonal =  1.0f / Mathf.Sqrt(Screen.width*Screen.width + Screen.height*Screen.height);
	        bool isMousePressed =  Mouse.current!=null  &&  Mouse.current.leftButton.isPressed;
	        bool isPenTip   =  Pen.current != null  &&  Pen.current.tip.isPressed;
	        bool isPenEraser = isPenEraserPressed();
	        if (isPenTip || isPenEraser){
	            Vector2 dt = Pen.current.delta.ReadValue();
	            dt.y *= -1; // Invert Y if necessary for tablet setup
	            dt *= normalizeByScreenDiagonal? inv_screenDiagonal : 1;
	            return dt;
	        }
	        if(isMousePressed){//CHECKING MOUSE ONLY IF PEN ISN'T PRESSED. Otherwise they fight and make huge deltas.
	            Vector2 dt = Mouse.current.delta.ReadValue();
	            dt *= normalizeByScreenDiagonal ? inv_screenDiagonal : 1;
	            return dt;
	        }
	        return Vector2.zero;
	    }


	    // NO NEED TO SCALE its output by Time.deltaTime
	    // See https://discussions.unity.com/t/mouse-sensitivity-changes-between-editor-and-built-exe/20038
	    public static Vector2 delta_while_RMBpressed( bool normalizeByScreenDiagonal=true ){
	        if (IsGlobalInputBlocked()) { return Vector2.zero; }
	        float inv_screenDiagonal =  1.0f / Mathf.Sqrt(Screen.width*Screen.width + Screen.height*Screen.height);
	        bool isMousePressed =  Mouse.current!=null  &&  Mouse.current.rightButton.isPressed;
	        bool isPenPressed   =  Pen.current != null  &&  Pen.current.firstBarrelButton.isPressed;
	        if (isPenPressed){
	            Vector2 dt =Pen.current.delta.ReadValue();
	            dt.y *= -1; // Invert Y if necessary for tablet setup
	            dt *= normalizeByScreenDiagonal? inv_screenDiagonal : 1;
	            return dt;
	        }
	        if(isMousePressed){//CHECKING MOUSE ONLY IF PEN ISN'T PRESSED. Otherwise they fight and make huge deltas.
	            Vector2 dt = Mouse.current.delta.ReadValue();
	            dt *= normalizeByScreenDiagonal ? inv_screenDiagonal : 1;
	            return dt;
	        }
	        return Vector2.zero;
	    }


	    // NO NEED TO SCALE its output by Time.deltaTime
	    // See https://discussions.unity.com/t/mouse-sensitivity-changes-between-editor-and-built-exe/20038
	    public static Vector2 delta_while_MMBpressed( bool normalizeByScreenDiagonal=true ){
	        if (IsGlobalInputBlocked()) { return Vector2.zero; }
	        float inv_screenDiagonal =  1.0f / Mathf.Sqrt(Screen.width*Screen.width + Screen.height*Screen.height);
	        bool isMousePressed =  Mouse.current!=null  &&  Mouse.current.middleButton.isPressed;
	        bool isPenPressed   =  Pen.current != null  &&  Pen.current.secondBarrelButton.isPressed;
	        if (isPenPressed){
	            Vector2 dt =Pen.current.delta.ReadValue();
	            dt.y *= -1; // Invert Y if necessary for tablet setup
	            dt *= normalizeByScreenDiagonal? inv_screenDiagonal : 1;
	            return dt;
	        }
	        if(isMousePressed){//CHECKING MOUSE ONLY IF PEN ISN'T PRESSED. Otherwise they fight and make huge deltas.
	            Vector2 dt = Mouse.current.delta.ReadValue();
	            dt *= normalizeByScreenDiagonal ? inv_screenDiagonal : 1;
	            return dt;
	        }
	        return Vector2.zero;
	    }


	    /// <summary>Screen position (pixel coords). Uses pen while tip/eraser down or while pen is in range (hover); after the stylus leaves the tablet, keeps the last pen/hover point until the mouse moves or a mouse button is pressed so brush/tools do not snap back to an idle mouse position. Requires Unity Input System Pen (e.g. Wacom).</summary>
	    public static Vector2 cursorScreenPos(){//entire window (NOT MAIN VIEW), pixel coords
	        Pen pen = Pen.current;
	        if (pen != null)
	        {
		        bool down = pen.tip.isPressed || pen.eraser.isPressed;
		        bool hover = pen.inRange.isPressed;
		        if (down || hover)
		        {
			        Vector2 p = pen.position.ReadValue();
			        _lastPenOrHoverScreenPos = p;
			        _haveLastPenOrHoverScreenPos = true;
			        return p;
		        }
	        }

	        if (Mouse.current != null)
	        {
		        bool mouseClickedThisFrame = Mouse.current.leftButton.wasPressedThisFrame
		                                     || Mouse.current.rightButton.wasPressedThisFrame
		                                     || Mouse.current.middleButton.wasPressedThisFrame;
		        if (mouseClickedThisFrame)
		        {
			        _haveLastPenOrHoverScreenPos = false;
			        return Mouse.current.position.ReadValue();
		        }
		        Vector2 d = Mouse.current.delta.ReadValue();
		        if (d.sqrMagnitude > MouseDeltaToBreakPenLatchSq)
		        {
			        _haveLastPenOrHoverScreenPos = false;
			        return Mouse.current.position.ReadValue();
		        }
		        if (_haveLastPenOrHoverScreenPos)
			        return _lastPenOrHoverScreenPos;
		        return Mouse.current.position.ReadValue();
	        }

	        if (pen != null)
	        {
		        Vector2 p = pen.position.ReadValue();
		        _lastPenOrHoverScreenPos = p;
		        _haveLastPenOrHoverScreenPos = true;
		        return p;
	        }

	        return _haveLastPenOrHoverScreenPos ? _lastPenOrHoverScreenPos : Vector2.zero;
	    }

	    /// <summary>Normalized [0,1] over entire window. Same source as cursorScreenPos (pen when drawing, else mouse/pen).</summary>
	    public static Vector2 cursorViewPos01(){//entire window (NOT MAIN VIEW) normalized in [0,1] range
	        Vector2 screenPos = cursorScreenPos();
	        screenPos /= new Vector2(Screen.width, Screen.height);
	        return screenPos;
	    }

	    //rectangle inside the entire window.
	    public static Vector2 cursorMainViewPos01(bool isInner_SD_view){
	        return isInner_SD_view? MainViewport_UI.instance.cursorInnerViewportPos01
	                              : MainViewport_UI.instance.cursorMainViewportPos01;
	    }

	    public static bool isKey_CtrlOrCommand_pressedThisFrame(){
	        if (Keyboard.current == null){ return false; }
	        if (Keyboard.current.ctrlKey.wasPressedThisFrame){ return true; }
	        if (Keyboard.current.leftCommandKey.wasPressedThisFrame){ return true; }
	        if (Keyboard.current.rightCommandKey.wasPressedThisFrame){ return true; }
	        return false;
	    }

	    public static bool isKey_CtrlOrCommand_pressed(){
	        if (Keyboard.current == null) { return false; }
	        if (Keyboard.current.ctrlKey.isPressed) { return true; }
	        if (Keyboard.current.leftCommandKey.isPressed){ return true; }
	        if (Keyboard.current.rightCommandKey.isPressed){ return true; }
	        return false;
	    }

	    public static bool isKey_Shift_pressed(){
	        if (Keyboard.current == null){ return false; }
	        if (Keyboard.current.shiftKey.isPressed) { return true; }
	        return false;
	    }
    
	    public static bool isKey_Shift_pressedThisFrame(){
	        if (Keyboard.current == null){ return false; }
	        return Keyboard.current.shiftKey.wasPressedThisFrame;
	    }

	    public static bool isKey_alt_pressed(){
	        if (Keyboard.current == null){ return false; }
	        if (Keyboard.current.altKey.isPressed) { return true; }
	        return false;
	    }

	    public static bool isKey_alt_pressedThisFrame(){
	        if (Keyboard.current == null){ return false; }
	        return Keyboard.current.altKey.wasPressedThisFrame;
	    }

	    // For example, text is being typed into an text prompt, etc.
	    // Usually we check it before recognising viewport shorcuts like 'R', or 'TAB'.
	    public static bool isSomeInputFieldActive() 
	        => EventSystem.current.currentSelectedGameObject != null &&
	           EventSystem.current.currentSelectedGameObject.GetComponent<TMP_InputField>()!=null &&
	           EventSystem.current.currentSelectedGameObject.activeInHierarchy;

	}
}//end namespace

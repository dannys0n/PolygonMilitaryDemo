using System;
using UnityEditor;
using UnityEngine;

// A flat, wheel-scrollable list instead of Unity's native multi-column popup menu.
public sealed class FpsPreviewDropdown : PopupWindowContent
{
    const float RowHeight=22;
    readonly string[] options;
    readonly Action<int> choose;
    readonly float width;
    int selected;
    Vector2 scroll;
    GUIStyle rowStyle;

    public FpsPreviewDropdown(string[] options,int selected,float width,Action<int> choose)
    {
        this.options=options;this.selected=selected;this.width=Mathf.Clamp(width,360,760);this.choose=choose;
        scroll.y=Mathf.Max(0,selected*RowHeight-320);
    }
    public override Vector2 GetWindowSize() => new Vector2(width,Mathf.Min(720,options.Length*RowHeight+4));
    public override void OnOpen() {editorWindow.wantsMouseMove=true;}
    public override void OnGUI(Rect rect)
    {
        if(rowStyle==null)rowStyle=new GUIStyle(EditorStyles.label){alignment=TextAnchor.MiddleLeft,padding=new RectOffset(10,4,2,2)};
        var e=Event.current;
        if(e.type==EventType.KeyDown)
        {
            int next=selected;
            if(e.keyCode==KeyCode.UpArrow)next--;
            else if(e.keyCode==KeyCode.DownArrow)next++;
            else if(e.keyCode==KeyCode.PageUp)next-=12;
            else if(e.keyCode==KeyCode.PageDown)next+=12;
            else if(e.keyCode==KeyCode.Home)next=0;
            else if(e.keyCode==KeyCode.End)next=options.Length-1;
            else if(e.keyCode==KeyCode.Return || e.keyCode==KeyCode.KeypadEnter){Select(selected);e.Use();return;}
            else if(e.keyCode==KeyCode.Escape){editorWindow.Close();e.Use();return;}
            else return;
            selected=Mathf.Clamp(next,0,options.Length-1);
            if(selected*RowHeight<scroll.y)scroll.y=selected*RowHeight;
            if((selected+1)*RowHeight>scroll.y+rect.height)scroll.y=(selected+1)*RowHeight-rect.height;
            e.Use();editorWindow.Repaint();
        }
        var viewport=new Rect(2,2,rect.width-4,rect.height-4);
        var content=new Rect(0,0,viewport.width-18,options.Length*RowHeight);
        scroll=GUI.BeginScrollView(viewport,scroll,content,false,content.height>viewport.height);
        int first=Mathf.Max(0,Mathf.FloorToInt(scroll.y/RowHeight));
        int last=Mathf.Min(options.Length,Mathf.CeilToInt((scroll.y+viewport.height)/RowHeight)+1);
        for(int i=first;i<last;i++)
        {
            var row=new Rect(0,i*RowHeight,content.width,RowHeight);
            if(i==selected)EditorGUI.DrawRect(row,new Color(.22f,.42f,.65f,.55f));
            else if(row.Contains(e.mousePosition))EditorGUI.DrawRect(row,new Color(.5f,.5f,.5f,.2f));
            if(GUI.Button(row,new GUIContent(options[i],options[i]),rowStyle))
            {
                GUI.EndScrollView();Select(i);return;
            }
        }
        GUI.EndScrollView();
        if(e.type==EventType.MouseMove)editorWindow.Repaint();
    }
    void Select(int index){choose(index);editorWindow.Close();}
}

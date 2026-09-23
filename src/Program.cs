using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace RisupEditor;

public static class Entry
{
    [STAThread] public static int Main(string[] args)
    {
        try {
            if(args.Length>=3 && args[0]=="--roundtrip"){RisupCodec.Save(RisupCodec.Load(args[1]),args[2]);return 0;}
            if(args.Length>=3 && args[0]=="--fixture-edit"){
                var p=RisupCodec.Load(args[1]);var b=p.Blocks??throw new Exception("no blocks");
                b[0].Set("text",Value.String("수정 완료\n{{char}}와 {{user}}\nUnicode: 🌿"));b.Reverse();RisupCodec.Save(p,args[2]);return 0;
            }
            var app=new Application(); Theme(app);
            if(args.Length>=2 && args[0]=="--search-test"){var w=new MainWindow();w.Loaded+=(_,_)=>w.RunSearchTest(args[1]);return app.Run(w);}
            if(args.Length>=2 && args[0]=="--diagnostic-test"){var w=new MainWindow();w.Loaded+=(_,_)=>w.RunDiagnosticTest(args[1]);return app.Run(w);}
            if(args.Length>=2 && args[0]=="--performance-test"){var w=new MainWindow();w.Loaded+=(_,_)=>w.RunPerformanceTest(args[1]);return app.Run(w);}
            if(args.Length>=2 && args[0]=="--self-test"){var w=new MainWindow();w.Loaded+=(_,_)=>w.RunSelfTest(args[1]);return app.Run(w);}
            var window=new MainWindow();if(args.Length>0)window.Loaded+=(_,_)=>window.OpenPaths(args);
            return app.Run(window);
        }catch(Exception ex){
            if(args.Length>0){System.Diagnostics.Trace.WriteLine(ex);return 1;}
            MessageBox.Show(ex.Message,"Risup Editor",MessageBoxButton.OK,MessageBoxImage.Error);return 1;
        }
    }
    static void Theme(Application app)
    {
        const string xaml="""
<ResourceDictionary xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
  <Style TargetType="Window"><Setter Property="FontFamily" Value="Segoe UI, Malgun Gothic"/><Setter Property="FontSize" Value="13"/><Setter Property="Background" Value="#F3F5F7"/><Setter Property="Foreground" Value="#243345"/></Style>
  <Style TargetType="Button"><Setter Property="Padding" Value="12,7"/><Setter Property="Margin" Value="3"/><Setter Property="Background" Value="White"/><Setter Property="Foreground" Value="#243345"/><Setter Property="BorderBrush" Value="#CBD5E1"/><Setter Property="Cursor" Value="Hand"/><Setter Property="Template"><Setter.Value><ControlTemplate TargetType="Button"><Border x:Name="b" CornerRadius="5" Background="{TemplateBinding Background}" BorderBrush="{TemplateBinding BorderBrush}" BorderThickness="1" Padding="{TemplateBinding Padding}"><ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/></Border><ControlTemplate.Triggers><Trigger Property="IsMouseOver" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="#0D9488"/></Trigger><Trigger Property="IsEnabled" Value="False"><Setter TargetName="b" Property="Opacity" Value="0.38"/></Trigger><Trigger Property="IsKeyboardFocused" Value="True"><Setter TargetName="b" Property="BorderBrush" Value="#0D9488"/><Setter TargetName="b" Property="BorderThickness" Value="2"/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
  <Style TargetType="TextBox"><Setter Property="Padding" Value="9,7"/><Setter Property="BorderBrush" Value="#D8E0E9"/><Setter Property="Background" Value="White"/><Setter Property="Foreground" Value="#243345"/><Setter Property="SelectionBrush" Value="#82CFC4"/><Setter Property="VerticalContentAlignment" Value="Center"/></Style>
  <Style TargetType="ComboBox"><Setter Property="Margin" Value="0,3,0,6"/><Setter Property="Padding" Value="6"/><Setter Property="MinHeight" Value="30"/></Style>
  <Style TargetType="TabItem"><Setter Property="Padding" Value="12,8"/></Style>
  <Style TargetType="ScrollViewer"><Setter Property="PanningMode" Value="VerticalOnly"/></Style>
</ResourceDictionary>
""";
        app.Resources=(ResourceDictionary)System.Windows.Markup.XamlReader.Parse(xaml);
    }
}

public sealed class EditorWindow : Window
{
    readonly List<Preset> references=new();
    readonly List<Value> blocks=new();
    readonly Stack<Snapshot> undo=new(),redo=new();
    readonly TabControl tabs=new(){BorderThickness=new Thickness(0),Background=Brushes.Transparent};
    readonly StackPanel work=new();
    readonly ScrollViewer workScroll=new(){VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};
    readonly TextBlock status=new(),workHint=new();
    readonly Button copyTab,copyBlock,undoButton,redoButton;
    Preset? basis;
    int referenceIndex=-1,selected=-1;
    string savedSignature="";
    bool dirty;
    readonly Dictionary<Value,Border> workCards=new();
    readonly Dictionary<Value,Action> refreshHeaders=new();
    record Snapshot(List<Value> Blocks,Preset? Basis,int Selected);
    static Brush Ink=>new SolidColorBrush(Color.FromRgb(36,51,69));
    static Brush Muted=>new SolidColorBrush(Color.FromRgb(105,121,140));
    static Brush Accent=>new SolidColorBrush(Color.FromRgb(15,118,110));
    static Brush Line=>new SolidColorBrush(Color.FromRgb(220,228,236));
    static TextBlock Text(string value,double size=13,Brush? color=null)=>new(){Text=value,FontSize=size,Foreground=color??Ink,TextWrapping=TextWrapping.Wrap};
    static Button Button(string text,Action action,bool accent=false){var b=new Button(){Content=text};if(accent){b.Background=Accent;b.Foreground=Brushes.White;b.BorderBrush=Accent;}b.Click+=(_,_)=>action();return b;}
    static Border Card(UIElement child)=>new(){Child=child,Background=Brushes.White,BorderBrush=Line,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8),Padding=new Thickness(14),Margin=new Thickness(0,0,0,12)};
    Preset? Current=>tabs.SelectedIndex>=0&&tabs.SelectedIndex<references.Count?references[tabs.SelectedIndex]:null;
    public EditorWindow()
    {
        Title="Risup Editor";Background=new SolidColorBrush(Color.FromRgb(243,245,247));FontFamily=new FontFamily("Segoe UI, Malgun Gothic");Width=1380;Height=900;MinWidth=1000;MinHeight=620;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        var root=new DockPanel(){Background=Background};Content=root;
        var bottom=new Border(){Padding=new Thickness(24,10,24,10),Background=Brushes.White,BorderBrush=Line,BorderThickness=new Thickness(0,1,0,0),Child=status};DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
        var columns=new Grid(){Margin=new Thickness(24,18,24,18)};columns.ColumnDefinitions.Add(new ColumnDefinition(){Width=new GridLength(1,GridUnitType.Star)});columns.ColumnDefinitions.Add(new ColumnDefinition(){Width=new GridLength(16)});columns.ColumnDefinitions.Add(new ColumnDefinition(){Width=new GridLength(1,GridUnitType.Star)});root.Children.Add(columns);
        var left=new DockPanel();Grid.SetColumn(left,0);columns.Children.Add(left);
        var lh=new StackPanel();DockPanel.SetDock(lh,Dock.Top);left.Children.Add(lh);lh.Children.Add(Text("참조 프리셋",20));lh.Children.Add(Text("읽기 전용 · 여러 파일을 탭으로 열어 비교할 수 있습니다.",12,Muted));
        var copies=new WrapPanel(){Margin=new Thickness(0,10,0,10)};copyTab=Button("현재 탭 복사 →",()=>Copy(true));copyBlock=Button("현재 블록 복사 →",()=>Copy(false));copies.Children.Add(Button("새 작업",NewWork));copies.Children.Add(Button("불러오기",OpenDialog));copies.Children.Add(copyTab);copies.Children.Add(copyBlock);lh.Children.Add(copies);
        tabs.SizeChanged+=(_,_)=>ResizeTabs();
        tabs.SelectionChanged+=(_,e)=>{if(e.Source==tabs){referenceIndex=-1;foreach(TabItem tab in tabs.Items)if(tab.Content is ScrollViewer viewer && viewer.Content is StackPanel panel)foreach(var c in panel.Children.OfType<Border>())c.BorderBrush=Line;Update();}};left.Children.Add(tabs);
        var splitter=new GridSplitter(){Width=5,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Stretch,Background=Line};Grid.SetColumn(splitter,1);columns.Children.Add(splitter);
        var right=new DockPanel();Grid.SetColumn(right,2);columns.Children.Add(right);
        var rh=new StackPanel();DockPanel.SetDock(rh,Dock.Top);right.Children.Add(rh);rh.Children.Add(Text("작업 중",20));workHint.Foreground=Muted;workHint.FontSize=12;rh.Children.Add(workHint);
        var toolbar=new WrapPanel(){Margin=new Thickness(0,10,0,8)};toolbar.Children.Add(Button("+ 새 블록",AddBlock));undoButton=Button("↶ 실행 취소",Undo);redoButton=Button("↷ 다시 실행",Redo);toolbar.Children.Add(undoButton);toolbar.Children.Add(redoButton);toolbar.Children.Add(Button("내보내기",Export,true));toolbar.Children.Add(Button("정보",About));rh.Children.Add(toolbar);
        workScroll.Content=work;right.Children.Add(workScroll);
        Closing+=(_,e)=>{if(!ConfirmDiscard())e.Cancel=true;};
        PreviewKeyDown+=(_,e)=>{if(Keyboard.Modifiers==ModifierKeys.Control){if(e.Key==Key.O){OpenDialog();e.Handled=true;}else if(e.Key==Key.S){Export();e.Handled=true;}else if(e.Key==Key.Z){Undo();e.Handled=true;}else if(e.Key==Key.Y){Redo();e.Handled=true;}}};
        savedSignature=Signature();RenderReferences();RenderWork();Update();
    }
    string Signature()=>Convert.ToBase64String(Value.Array(blocks).Encode())+"|"+(basis==null?"":Convert.ToBase64String(basis.Data.Encode()));
    void Update(string? message=null)
    {
        foreach(var refresh in refreshHeaders.Values)refresh();
        dirty=Signature()!=savedSignature;Title=$"{(dirty?"● ":"")}Risup Editor";
        workHint.Text=$"{blocks.Count}개 블록 · {(dirty?"내보내지 않은 변경 사항이 있습니다":"변경 사항 없음")}";
        copyTab.IsEnabled=Current?.Blocks is {} list&&list.Count>0;copyBlock.IsEnabled=Current?.Blocks is {} b&&referenceIndex>=0&&referenceIndex<b.Count;
        undoButton.IsEnabled=undo.Count>0;redoButton.IsEnabled=redo.Count>0;if(message!=null)status.Text=message;else if(string.IsNullOrEmpty(status.Text))status.Text=".risup 파일을 불러와 시작하세요.  ·  Ctrl+O 불러오기  /  Ctrl+S 내보내기";
    }
    Snapshot Capture()=>new(blocks.Select(b=>b.Clone()).ToList(),basis,selected);
    void Remember(){undo.Push(Capture());redo.Clear();}
    void Restore(Snapshot s){blocks.Clear();blocks.AddRange(s.Blocks.Select(b=>b.Clone()));basis=s.Basis;selected=s.Selected;RenderWork();Update();}
    void Undo(){if(undo.Count==0)return;redo.Push(Capture());Restore(undo.Pop());Update("변경을 취소했습니다.");}
    void Redo(){if(redo.Count==0)return;undo.Push(Capture());Restore(redo.Pop());Update("변경을 다시 적용했습니다.");}
    bool ConfirmDiscard()=>!dirty||MessageBox.Show(this,"내보내지 않은 변경 사항이 있습니다. 버리고 계속할까요?","변경 사항 확인",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes;
    void NewWork(){if(!ConfirmDiscard())return;blocks.Clear();basis=null;selected=-1;undo.Clear();redo.Clear();savedSignature=Signature();RenderWork();Update("새 작업을 시작했습니다.");}
    void OpenDialog(){var d=new OpenFileDialog(){Filter="RisuAI 프리셋|*.risup;*.risupreset",Multiselect=true};if(d.ShowDialog(this)==true)OpenPaths(d.FileNames);}
    public void OpenPaths(IEnumerable<string> paths)
    {
        var errors=new List<string>();int added=0;
        foreach(var path in paths){try{var p=RisupCodec.Load(path);references.Add(p);added++;}catch(Exception ex){errors.Add(System.IO.Path.GetFileName(path)+": "+ex.Message);}}
        if(added>0){RenderReferences();tabs.SelectedIndex=references.Count-1;Update($"참조 프리셋 {added}개를 불러왔습니다.");}
        if(errors.Count>0)MessageBox.Show(this,string.Join("\n\n",errors),"불러오기 실패",MessageBoxButton.OK,MessageBoxImage.Warning);
    }
    void RenderReferences()
    {
        int old=tabs.SelectedIndex;tabs.Items.Clear();
        foreach(var p in references){
            var tab=new TabItem(){HorizontalContentAlignment=HorizontalAlignment.Stretch,MinWidth=0};var head=new Grid();head.ColumnDefinitions.Add(new ColumnDefinition(){Width=new GridLength(1,GridUnitType.Star)});head.ColumnDefinitions.Add(new ColumnDefinition(){Width=GridLength.Auto});var tabTitle=Text(p.Name,12);tabTitle.TextWrapping=TextWrapping.NoWrap;tabTitle.TextTrimming=TextTrimming.CharacterEllipsis;tabTitle.VerticalAlignment=VerticalAlignment.Center;head.Children.Add(tabTitle);
            var close=Button("×",()=>{int i=references.IndexOf(p);references.Remove(p);RenderReferences();Update("참조 탭을 닫았습니다. 작업 중인 내용은 유지됩니다.");});close.Padding=new Thickness(2,0,2,0);close.Margin=new Thickness(2,0,0,0);close.ToolTip="참조 탭 닫기";head.Children.Add(close);tab.Header=head;tab.ToolTip=p.Path;
            Grid.SetColumn(close,1);
            var body=new StackPanel(){Margin=new Thickness(0,12,8,0)};
            if(p.Blocks==null)body.Children.Add(Card(Text("이 프리셋에는 블록형 promptTemplate이 없습니다. 구형 프롬프트 구성의 자동 변환은 지원하지 않습니다.",14,Muted)));
            else if(p.Blocks.Count==0)body.Children.Add(Card(Text("프롬프트 블록이 없는 프리셋입니다.",14,Muted)));
            else for(int i=0;i<p.Blocks.Count;i++){int index=i;var value=p.Blocks[i];var card=BuildBlock(value,i,false);card.PreviewMouseLeftButtonDown+=(_,_)=>{referenceIndex=index;foreach(var child in body.Children.OfType<Border>())child.BorderBrush=Line;card.BorderBrush=Accent;Update($"참조 블록 {index+1} 선택: {Label(value)}");};body.Children.Add(card);}
            tab.Content=new ScrollViewer(){Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};tabs.Items.Add(tab);
        }
        if(references.Count==0){var empty=new TabItem(){Header="시작하기",Content=Card(Text("참조할 .risup 파일을 불러오세요.\n\n여러 탭을 열어 비교하고, 필요한 블록을 오른쪽으로 복사할 수 있습니다.",15,Muted))};tabs.Items.Add(empty);}
        tabs.SelectedIndex=Math.Clamp(old,0,tabs.Items.Count-1);referenceIndex=-1;Dispatcher.BeginInvoke(ResizeTabs);Update();
    }
    void ResizeTabs()
    {
        if(references.Count==0||tabs.ActualWidth<=0)return;
        double width=Math.Max(1,Math.Floor((tabs.ActualWidth-20)/references.Count)-8);
        foreach(var tab in tabs.Items.OfType<TabItem>()){tab.Width=width;tab.Padding=new Thickness(references.Count>6?4:12,8,references.Count>6?4:12,8);}
    }
    void Copy(bool all)
    {
        var p=Current;if(p?.Blocks==null)return;var incoming=all?p.Blocks:referenceIndex>=0&&referenceIndex<p.Blocks.Count?new List<Value>{p.Blocks[referenceIndex]}:new();if(incoming.Count==0)return;
        Remember();basis??=p.Clone();int at=selected>=0?selected+1:blocks.Count;blocks.InsertRange(at,incoming.Select(b=>b.Clone()));selected=at;RenderWork();FocusSelected();Update($"{incoming.Count}개 블록을 작업 중에 복사했습니다.");
    }
    static readonly Dictionary<string,string> Types=new(){["plain"]="일반 프롬프트",["jailbreak"]="탈옥 프롬프트",["cot"]="사고 지침",["chatML"]="ChatML",["description"]="캐릭터 설명",["persona"]="페르소나",["lorebook"]="로어북",["authornote"]="작가의 노트",["memory"]="메모리",["chat"]="채팅 기록",["postEverything"]="마지막 삽입 영역",["cache"]="캐시 지점"};
    static string TypeName(Value b)=>Types.GetValueOrDefault(b.Str("type"),b.Str("type"));
    static string Label(Value b)=>string.IsNullOrEmpty(b.Str("name"))?TypeName(b):b.Str("name");
    static bool Plain(Value b)=>b.Str("type") is "plain" or "jailbreak" or "cot" or "chatML";
    static bool Inner(Value b)=>b.Str("type") is "description" or "persona" or "authornote" or "memory";
    void Select(int i){selected=i;foreach(var pair in workCards)pair.Value.BorderBrush=blocks.IndexOf(pair.Key)==i?Accent:Line;}
    void RenderWork()
    {
        work.Children.Clear();workCards.Clear();refreshHeaders.Clear();
        if(blocks.Count==0){work.Children.Add(Card(Text("아직 블록이 없습니다.\n\n참조에서 현재 탭이나 블록을 복사하거나, 새 블록을 추가하세요.",15,Muted)));return;}
        for(int i=0;i<blocks.Count;i++){var b=blocks[i];var card=BuildBlock(b,i,true);workCards[b]=card;work.Children.Add(card);}Select(selected);
    }
    void FocusSelected(){Dispatcher.BeginInvoke(()=>{if(selected>=0&&selected<blocks.Count&&workCards.TryGetValue(blocks[selected],out var card))card.BringIntoView();});}
    Border BuildBlock(Value b,int index,bool editable)
    {
        var stack=new StackPanel();var card=Card(stack);card.Margin=new Thickness(0,0,8,12);
        var top=new DockPanel();stack.Children.Add(top);
        if(editable){
            card.PreviewMouseDown+=(_,_)=>Select(blocks.IndexOf(b));card.GotKeyboardFocus+=(_,_)=>Select(blocks.IndexOf(b));
            var commands=new StackPanel(){Orientation=Orientation.Horizontal};DockPanel.SetDock(commands,Dock.Right);top.Children.Add(commands);
            foreach(var cmd in new[]{("↑",(Action)(()=>Move(b,-1)),"위로 이동"),("↓",(Action)(()=>Move(b,1)),"아래로 이동"),("⧉",(Action)(()=>Duplicate(b)),"블록 복제"),("×",(Action)(()=>Delete(b)),"블록 삭제")}){var button=Button(cmd.Item1,cmd.Item2);button.Padding=new Thickness(6,2,6,2);button.ToolTip=cmd.Item3;commands.Children.Add(button);}
            var handle=Text("⠿",20,Muted);handle.Cursor=Cursors.SizeAll;handle.ToolTip="드래그하여 이동";handle.Margin=new Thickness(0,0,8,0);DockPanel.SetDock(handle,Dock.Left);top.Children.Add(handle);
            Point? start=null;handle.MouseLeftButtonDown+=(_,e)=>start=e.GetPosition(handle);handle.MouseMove+=(_,e)=>{if(start is {} pt&&e.LeftButton==MouseButtonState.Pressed&&(e.GetPosition(handle)-pt).Length>5){start=null;DragDrop.DoDragDrop(handle,new DataObject("RisupEditor.Block",b),DragDropEffects.Move);}};
            card.AllowDrop=true;card.DragOver+=(_,e)=>{e.Effects=e.Data.GetDataPresent("RisupEditor.Block")?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;};
            card.Drop+=(_,e)=>{if(e.Data.GetData("RisupEditor.Block") is Value source){int from=blocks.IndexOf(source),to=blocks.IndexOf(b);if(from>=0&&to>=0&&from!=to){bool after=e.GetPosition(card).Y>card.ActualHeight/2;Remember();blocks.RemoveAt(from);to=blocks.IndexOf(b)+(after?1:0);blocks.Insert(to,source);selected=to;RenderWork();FocusSelected();Update("블록 순서를 변경했습니다.");}}e.Handled=true;};
        }
        var title=Text($"{index+1:00}   {Label(b)}",15);title.FontWeight=FontWeights.SemiBold;top.Children.Add(title);
        var subtitle=new TextBlock(){Text=TypeName(b)+ (b.Str("role",b.Str("role2")) is {Length:>0} r?"  ·  "+r:""),Foreground=Muted,FontSize=11,Margin=new Thickness(0,5,0,10)};stack.Children.Add(subtitle);
        if(editable)refreshHeaders[b]=()=>{title.Text=$"{index+1:00}   {Label(b)}";subtitle.Text=TypeName(b)+(b.Str("role",b.Str("role2")) is {Length:>0} role?"  ·  "+role:"");};
        if(Plain(b))stack.Children.Add(Field(b,"text",editable,true));
        else {var note=b.Str("type") switch{"chat"=>$"이 자리에 채팅 기록이 들어갑니다.\n범위: {Display(b.Get("rangeStart"))} → {Display(b.Get("rangeEnd"))}","cache"=>"이 위치에 캐시 지점이 설정됩니다.","postEverything"=>"이 자리에 마지막 삽입 영역의 내용이 들어갑니다.",_=>Types.ContainsKey(b.Str("type"))?$"이 자리에 {TypeName(b)} 내용이 들어갑니다.":"알 수 없는 블록 유형입니다. 원본 설정을 보존합니다."};stack.Children.Add(new Border(){Background=new SolidColorBrush(Color.FromRgb(239,246,246)),Padding=new Thickness(14),CornerRadius=new CornerRadius(5),Child=Text(note,13,Accent)});}
        var settings=new StackPanel(){Margin=new Thickness(0,8,0,0)};
        AddField(settings,"블록 이름",b,"name",editable);
        if(b.Str("type") is "plain" or "jailbreak" or "cot"){AddChoice(settings,"역할",b,"role",new[]{"system","user","bot"},editable);AddChoice(settings,"구분",b,"type2",new[]{"normal","main","globalNote"},editable);}
        if(Inner(b)){AddChoice(settings,"역할",b,"role2",new[]{"system","user","bot"},editable);AddField(settings,"내부 형식 · {{slot}} 자리에 내용 삽입",b,"innerFormat",editable,true);}
        if(b.Str("type")=="authornote")AddField(settings,"기본 작가의 노트",b,"defaultText",editable,true);
        if(b.Str("type")=="chat"){AddNumber(settings,"시작 범위 · -1000은 전체 기록",b,"rangeStart",editable);AddNumber(settings,"끝 범위 · end는 마지막까지",b,"rangeEnd",editable,true);AddBool(settings,"시스템 채팅에서 원래 역할 유지",b,"chatAsOriginalOnSystem",editable);}
        if(b.Str("type")=="cache"){AddNumber(settings,"깊이",b,"depth",editable);AddChoice(settings,"역할",b,"role",new[]{"all","user","assistant","system"},editable);}
        var expander=new Expander(){Header="블록 설정",Content=settings,Margin=new Thickness(0,10,0,0)};stack.Children.Add(expander);
        return card;
    }
    static string Display(Value? v)=>v?.Text()??v?.Number()?.ToString()??"미지정";
    TextBox Field(Value b,string key,bool edit,bool multi)
    {
        var t=new TextBox(){Text=b.Str(key),IsReadOnly=!edit,AcceptsReturn=multi,AcceptsTab=multi,TextWrapping=TextWrapping.Wrap,MinHeight=multi?105:32,MaxHeight=multi?460:double.PositiveInfinity,VerticalScrollBarVisibility=multi?ScrollBarVisibility.Auto:ScrollBarVisibility.Disabled,FontFamily=multi?new FontFamily("Consolas, Malgun Gothic"):FontFamily,FontSize=13,VerticalContentAlignment=multi?VerticalAlignment.Top:VerticalAlignment.Center,IsUndoEnabled=false};
        bool captured=false;t.LostKeyboardFocus+=(_,_)=>captured=false;
        if(edit)t.TextChanged+=(_,_)=>{if(!captured){Remember();captured=true;}b.Set(key,Value.String(t.Text));Update();};return t;
    }
    void AddField(Panel panel,string label,Value b,string key,bool edit,bool multi=false){panel.Children.Add(new TextBlock(){Text=label,Foreground=Muted,Margin=new Thickness(0,7,0,4),FontSize=11});panel.Children.Add(Field(b,key,edit,multi));}
    void AddChoice(Panel panel,string label,Value b,string key,string[] choices,bool edit)
    {
        panel.Children.Add(Text(label,11,Muted));string current=b.Str(key);var opts=choices.ToList();if(!opts.Contains(current))opts.Insert(0,current);
        var combo=new ComboBox(){ItemsSource=opts,SelectedItem=current,IsEnabled=edit};combo.SelectionChanged+=(_,_)=>{if(combo.SelectedItem is string value&&value!=b.Str(key)){Remember();b.Set(key,Value.String(value));Update();}};panel.Children.Add(combo);
    }
    void AddNumber(Panel panel,string label,Value b,string key,bool edit,bool allowEnd=false)
    {
        panel.Children.Add(Text(label,11,Muted));var t=new TextBox(){Text=Display(b.Get(key)),IsReadOnly=!edit,Margin=new Thickness(0,3,0,6)};
        void Commit(){if(!edit||t.Text==Display(b.Get(key)))return;if(allowEnd&&t.Text=="end"){Remember();b.Set(key,Value.String("end"));}else if(long.TryParse(t.Text,out var n)&&n>=-1000000&&n<=1000000){Remember();b.Set(key,Value.Int(n));}else{t.Text=Display(b.Get(key));Update("숫자 범위는 -1000000~1000000입니다. 끝 범위에는 end도 사용할 수 있습니다.");return;}Update();}
        t.LostKeyboardFocus+=(_,_)=>Commit();t.KeyDown+=(_,e)=>{if(e.Key==Key.Enter)Commit();};panel.Children.Add(t);
    }
    void AddBool(Panel panel,string label,Value b,string key,bool edit){var cb=new CheckBox(){Content=label,IsChecked=b.Get(key)?.Boolean()??false,IsEnabled=edit,Margin=new Thickness(0,6,0,6)};cb.Click+=(_,_)=>{Remember();b.Set(key,Value.Bool(cb.IsChecked==true));Update();};panel.Children.Add(cb);}
    void Move(Value b,int offset){int from=blocks.IndexOf(b),to=from+offset;if(from<0||to<0||to>=blocks.Count)return;Remember();blocks.RemoveAt(from);blocks.Insert(to,b);selected=to;RenderWork();FocusSelected();Update("블록을 이동했습니다.");}
    void Duplicate(Value b){int i=blocks.IndexOf(b);Remember();blocks.Insert(i+1,b.Clone());selected=i+1;RenderWork();FocusSelected();Update("블록을 복제했습니다.");}
    void Delete(Value b){Remember();int i=blocks.IndexOf(b);blocks.Remove(b);selected=Math.Min(i,blocks.Count-1);RenderWork();Update("블록을 삭제했습니다. 실행 취소로 복원할 수 있습니다.");}
    void AddBlock()
    {
        var dialog=Dialog("새 블록",400,240,out var panel);panel.Children.Add(Text("추가할 블록 유형을 선택하세요.",15));var combo=new ComboBox(){ItemsSource=Types.ToList(),DisplayMemberPath="Value",SelectedIndex=0,Margin=new Thickness(0,16,0,16)};panel.Children.Add(combo);
        panel.Children.Add(Button("추가",()=>dialog.DialogResult=true,true));if(dialog.ShowDialog()!=true)return;
        var key=((KeyValuePair<string,string>)combo.SelectedItem).Key;var b=Value.Map();b.Set("type",Value.String(key));
        if(Plain(b)){b.Set("text",Value.String(""));if(key!="chatML"){b.Set("role",Value.String("system"));b.Set("type2",Value.String("normal"));}}
        if(Inner(b))b.Set("role2",Value.String("system"));if(key=="chat"){b.Set("rangeStart",Value.Int(-1000));b.Set("rangeEnd",Value.String("end"));}if(key=="cache"){b.Set("depth",Value.Int(1));b.Set("role",Value.String("all"));b.Set("name",Value.String("캐시 지점"));}
        Remember();int at=selected>=0?selected+1:blocks.Count;blocks.Insert(at,b);selected=at;RenderWork();FocusSelected();Update("새 블록을 추가했습니다.");
    }
    Window Dialog(string title,int width,int height,out StackPanel panel){panel=new StackPanel(){Margin=new Thickness(24)};return new Window(){Title=title,Width=width,Height=height,Owner=this,WindowStartupLocation=WindowStartupLocation.CenterOwner,ResizeMode=ResizeMode.NoResize,Content=panel};}
    void Export()
    {
        Keyboard.ClearFocus();basis??=Current?.Clone()??references.FirstOrDefault()?.Clone();if(basis==null){MessageBox.Show(this,"내보내기 전에 참조 프리셋을 하나 불러오세요.","내보내기");return;}
        var d=Dialog("프리셋 내보내기",550,320,out var panel);panel.Children.Add(Text("작업 중인 프롬프트를 .risup으로 저장합니다.",16));panel.Children.Add(Text($"{blocks.Count}개 블록 · 기준: {basis.Name}\n기준 프리셋의 모델·생성 설정을 함께 저장합니다.",12,Muted));
        panel.Children.Add(new TextBlock(){Text="RisuAI에 표시할 프리셋 이름",Margin=new Thickness(0,18,0,6)});var name=new TextBox(){Text=basis.Name+" · 작업본"};panel.Children.Add(name);panel.Children.Add(Button("파일 이름과 위치 선택",()=>{if(!string.IsNullOrWhiteSpace(name.Text))d.DialogResult=true;},true));if(d.ShowDialog()!=true)return;
        var safe=string.Concat(name.Text.Select(c=>System.IO.Path.GetInvalidFileNameChars().Contains(c)?'_':c));var save=new SaveFileDialog(){Filter="RisuAI 프리셋|*.risup",DefaultExt=".risup",AddExtension=true,FileName=safe+".risup",OverwritePrompt=true};if(save.ShowDialog(this)!=true)return;
        try{var output=basis.Clone();output.Data.Set("name",Value.String(name.Text.Trim()));output.Data.Set("promptTemplate",Value.Array(blocks.Select(b=>b.Clone())));RisupCodec.Save(output,save.FileName);savedSignature=Signature();Update("내보내기 완료: "+save.FileName);}catch(Exception ex){MessageBox.Show(this,ex.Message,"내보내기 실패",MessageBoxButton.OK,MessageBoxImage.Error);}
    }
    void About()
    {
        var d=Dialog("Risup Editor 정보",700,550,out var p);p.Children.Add(Text("Risup Editor 0.1.0",23));p.Children.Add(Text("로컬 프리셋 편집기 · AGPL-3.0\nRPack 포맷 리소스: Copyright (c) 2026 Kwaroran\n원본: RisuAI-reference/src/ts/rpack\n이 프로그램은 프롬프트를 실행하거나 외부 서버로 전송하지 않습니다.",13,Muted));
        using var s=typeof(EditorWindow).Assembly.GetManifestResourceStream("RisupEditor.LICENSE-AGPL.txt")!;using var reader=new StreamReader(s);p.Children.Add(new TextBox(){Text=reader.ReadToEnd(),IsReadOnly=true,TextWrapping=TextWrapping.Wrap,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Height=300,Margin=new Thickness(0,15,0,0)});d.ShowDialog();
    }
    public async void RunSelfTest(string folder)
    {
        try {
            string fixture=System.IO.Path.Combine(folder,"reference.risup");for(int i=0;i<10;i++)references.Add(RisupCodec.Load(fixture));RenderReferences();tabs.SelectedIndex=0;Copy(true);
            if(blocks.Count!=references[0].Blocks!.Count)throw new Exception("tab copy");
            string original=references[0].Blocks![0].Str("text");var textbox=Descendants<TextBox>(workCards[blocks[0]]).First();textbox.Text="UI 편집 테스트";
            if(blocks[0].Str("text")!="UI 편집 테스트"||references[0].Blocks![0].Str("text")!=original)throw new Exception("editing/reference isolation");
            Undo();if(blocks[0].Str("text")!=original)throw new Exception("undo edit");Redo();if(blocks[0].Str("text")!="UI 편집 테스트")throw new Exception("redo edit");
            var moved=blocks[0];Move(moved,1);if(blocks[1]!=moved)throw new Exception("move");Undo();
            referenceIndex=0;int count=blocks.Count;Copy(false);if(blocks.Count!=count+1)throw new Exception("block copy");Undo();
            Duplicate(blocks[0]);if(blocks.Count!=count+1)throw new Exception("duplicate");Delete(blocks[selected]);if(blocks.Count!=count)throw new Exception("delete");Undo();Undo();
            var output=basis!.Clone();output.Data.Set("promptTemplate",Value.Array(blocks.Select(b=>b.Clone())));RisupCodec.Save(output,System.IO.Path.Combine(folder,"ui-output.risup"));
            if(RisupCodec.Load(System.IO.Path.Combine(folder,"ui-output.risup")).Blocks![0].Str("text")!="UI 편집 테스트")throw new Exception("save edited UI");
            savedSignature=Signature();Update("자체 검증 완료 · 탭/블록 복사, 원본 보존, 편집, 이동, 복제·삭제, 실행 취소·다시 실행, 저장");
            await System.Threading.Tasks.Task.Delay(400);UpdateLayout();var tabTops=tabs.Items.OfType<TabItem>().Select(t=>Math.Round(t.TranslatePoint(new Point(0,0),tabs).Y)).ToArray();if(tabTops.Max()-tabTops.Min()>3)throw new Exception($"reference tabs wrapped to multiple rows; host={tabs.ActualWidth}, item={tabs.Items.OfType<TabItem>().First().ActualWidth}, tops={string.Join(',',tabTops)}");var surface=(FrameworkElement)Content;var bitmap=new RenderTargetBitmap((int)surface.ActualWidth,(int)surface.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(surface);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var f=File.Create(System.IO.Path.Combine(folder,"ui-preview.png")))png.Save(f);
            File.WriteAllText(System.IO.Path.Combine(folder,"ui-test-result.txt"),"PASS: tab copy, block copy, reference immutability, actual TextBox editing, move, duplicate, delete, undo, redo, save/reopen");dirty=false;Application.Current.Shutdown(0);
        }catch(Exception ex){File.WriteAllText(System.IO.Path.Combine(folder,"ui-test-result.txt"),ex.ToString());dirty=false;Application.Current.Shutdown(1);}
    }
    static IEnumerable<T> Descendants<T>(DependencyObject root) where T:DependencyObject{for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T t)yield return t;foreach(var x in Descendants<T>(child))yield return x;}}
}




using System.Globalization;

namespace RfcEditor;
public sealed class EditorForm : Form
{
    readonly ListBox components=new(){Dock=DockStyle.Fill,IntegralHeight=false,Font=new Font("Microsoft YaHei UI",10)};
    readonly TextBox componentSearch=new(){PlaceholderText="搜索武器或子弹名称",Dock=DockStyle.Top};
    readonly TextBox fieldSearch=new(){PlaceholderText="搜索字段：伤害 / damage / 弹匣 / ammo …",Width=370};
    readonly ComboBox kind=new(){DropDownStyle=ComboBoxStyle.DropDownList,Width=130};
    readonly CheckBox references=new(){Text="显示资源引用",AutoSize=true};
    readonly Label title=new(){AutoSize=true,Font=new Font("Microsoft YaHei UI",14,FontStyle.Bold),Margin=new Padding(0,0,0,8)};
    readonly Label detail=new(){AutoSize=true,MaximumSize=new Size(1100,0),Margin=new Padding(0,0,0,10)};
    readonly Label relation=new(){AutoSize=true,ForeColor=Color.FromArgb(22,97,125),MaximumSize=new Size(1100,0),Margin=new Padding(0,0,0,10)};
    readonly DataGridView grid=new(){Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AllowUserToResizeRows=false,RowHeadersVisible=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,SelectionMode=DataGridViewSelectionMode.CellSelect,BackgroundColor=Color.White,BorderStyle=BorderStyle.None,MultiSelect=false};
    readonly ToolStripStatusLabel status=new(){Spring=true,TextAlign=ContentAlignment.MiddleLeft};
    readonly ToolStripButton save=new("另存 RFC"){Enabled=false};
    readonly ToolStripButton saveCurrent=new(){Enabled=false};
    readonly ToolStripButton open=new();
    readonly ToolStripDropDownButton language=new();
    readonly ToolStripButton reset=new("还原当前组件"){Enabled=false};
    readonly ToolStripButton log=new("解析报告");
    readonly string? initialPath;
    BundleDocument? document;
    Component? current;
    bool populating;
    bool busy;
    static string T(string key,params object[] args)=>L10n.T(key,args);
    public EditorForm(string? initial)
    {
        initialPath=initial;Text="RFC 武器参数编辑器";Width=1280;Height=820;MinimumSize=new Size(900,600);StartPosition=FormStartPosition.CenterScreen;
        Font=new Font("Microsoft YaHei UI",9);BackColor=Color.FromArgb(244,247,250);AllowDrop=true;
        var tools=new ToolStrip(){GripStyle=ToolStripGripStyle.Hidden,Padding=new Padding(8),ImageScalingSize=new Size(20,20)};
        open.Click+=async(_,_)=>{if(busy)return;using var d=new OpenFileDialog(){Filter=T("filter")};if(d.ShowDialog()==DialogResult.OK)await Open(d.FileName);};
        tools.Items.AddRange(new ToolStripItem[]{open,new ToolStripSeparator(),saveCurrent,save,reset,new ToolStripSeparator(),log,language});
        language.DropDownItems.Add("简体中文",null,(_,_)=>SwitchLanguage(false));
        language.DropDownItems.Add("English",null,(_,_)=>SwitchLanguage(true));
        saveCurrent.Click+=async(_,_)=>await SaveCurrent();
        save.Click+=async(_,_)=>await SaveAs(); reset.Click+=(_,_)=>Reset();log.Click+=(_,_)=>MessageBox.Show(this,document==null?T("noFile"):T("reportText",document.UnityVersion,document.Components.Count,document.Diagnostics.Count==0?T("noWarnings"):string.Join("\n",document.Diagnostics)),T("report"));
        var split=new SplitContainer(){Dock=DockStyle.Fill,FixedPanel=FixedPanel.Panel1,SplitterWidth=6};
        split.Size=new Size(1250,700);split.SplitterDistance=310;
        split.Panel1.Padding=new Padding(12);split.Panel2.Padding=new Padding(16);
        kind.Items.AddRange(new object[]{"全部组件","Projectile","Weapon"});kind.SelectedIndex=0;
        var leftHeader=new FlowLayoutPanel(){Dock=DockStyle.Top,Height=46,FlowDirection=FlowDirection.LeftToRight};leftHeader.Controls.Add(kind);
        split.Panel1.Controls.Add(components);split.Panel1.Controls.Add(componentSearch);split.Panel1.Controls.Add(leftHeader);
        var header=new FlowLayoutPanel(){Dock=DockStyle.Top,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false};
        header.Controls.Add(title);header.Controls.Add(detail);header.Controls.Add(relation);
        var searchRow=new FlowLayoutPanel(){AutoSize=true,Margin=new Padding(0,0,0,12)};searchRow.Controls.Add(fieldSearch);searchRow.Controls.Add(references);header.Controls.Add(searchRow);
        grid.Columns.Add(new DataGridViewTextBoxColumn(){Name="label",HeaderText="说明",ReadOnly=true,FillWeight=24});
        grid.Columns.Add(new DataGridViewTextBoxColumn(){Name="path",HeaderText="字段路径",ReadOnly=true,FillWeight=43});
        grid.Columns.Add(new DataGridViewTextBoxColumn(){Name="type",HeaderText="类型",ReadOnly=true,FillWeight=13});
        grid.Columns.Add(new DataGridViewTextBoxColumn(){Name="value",HeaderText="数值（双击修改）",FillWeight=22});
        grid.Columns.Add(new DataGridViewTextBoxColumn(){Name="original",HeaderText="打开时原值",ReadOnly=true,FillWeight=20});
        grid.ColumnHeadersHeight=38;grid.RowTemplate.Height=29;grid.EnableHeadersVisualStyles=false;grid.ColumnHeadersDefaultCellStyle.BackColor=Color.FromArgb(225,234,242);grid.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(248,250,252);
        split.Panel2.Controls.Add(grid);split.Panel2.Controls.Add(header);
        var strip=new StatusStrip(){AutoSize=false,Height=28,SizingGrip=false};strip.Items.Add(status);Controls.Add(split);Controls.Add(tools);Controls.Add(strip);tools.Dock=DockStyle.Top;
        components.SelectedIndexChanged+=(_,_)=>{if(populating)return;current=components.SelectedItem as Component;ShowFields();};
        componentSearch.TextChanged+=(_,_)=>FilterComponents();kind.SelectedIndexChanged+=(_,_)=>FilterComponents();
        fieldSearch.TextChanged+=(_,_)=>ShowFields();references.CheckedChanged+=(_,_)=>ShowFields();
        grid.CellValidating+=ValidateCell;grid.CellEndEdit+=(_,_)=>{UpdateStatus();RefreshComponentNames();};
        grid.CurrentCellDirtyStateChanged+=(_,_)=>UpdateStatus();
        grid.DataError+=(_,e)=>{e.ThrowException=false;};
        FormClosing+=(_,e)=>{if(busy){e.Cancel=true;return;}if(!grid.EndEdit() || !ConfirmDiscard())e.Cancel=true;};
        DragEnter+=(_,e)=>{if(!busy && e.Data?.GetDataPresent(DataFormats.FileDrop)==true)e.Effect=DragDropEffects.Copy;};
        DragDrop+=async(_,e)=>{if(e.Data?.GetData(DataFormats.FileDrop) is string[] paths && paths.Length>0)await Open(paths[0]);};
        Shown+=async(_,_)=>{if(initialPath!=null)await Open(initialPath);};
        title.Text="打开 Ravenfield 武器模组";detail.Text="选择 .rfc 或将文件拖到窗口。Projectile 和 Weapon 会自动分类。";
        try{L10n.English=File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"language.txt")).Trim()=="en";}catch{}
        ApplyLanguage();
    }
    bool ConfirmDiscard()=>document?.ChangeCount is not >0 || MessageBox.Show(this,T("discard"),T("discardTitle"),MessageBoxButtons.OKCancel,MessageBoxIcon.Question)==DialogResult.OK;
    void SwitchLanguage(bool english)
    {
        if(busy || !grid.EndEdit())return;
        L10n.English=english;
        try{File.WriteAllText(Path.Combine(AppContext.BaseDirectory,"language.txt"),english?"en":"zh");}catch{}
        ApplyLanguage();
    }
    void ApplyLanguage()
    {
        open.Text=T("open");saveCurrent.Text=T("save")+" (Ctrl+S)";save.Text=T("saveAs");reset.Text=T("reset");log.Text=T("report");
        language.Text=L10n.English?"Language: English":"语言：简体中文";
        ((ToolStripMenuItem)language.DropDownItems[0]).Checked=!L10n.English;
        ((ToolStripMenuItem)language.DropDownItems[1]).Checked=L10n.English;
        componentSearch.PlaceholderText=T("componentSearch");fieldSearch.PlaceholderText=T("fieldSearch");references.Text=T("references");
        int selected=kind.SelectedIndex;populating=true;kind.Items[0]=T("all");kind.SelectedIndex=selected;populating=false;
        foreach(string key in new[]{"label","path","type","value","original"})grid.Columns[key]!.HeaderText=T(key);
        Text=T("app")+" v1.1"+(document==null?"":" — "+Path.GetFileName(document.Source));
        if(document==null){title.Text=T("welcome");detail.Text=T("intro");}else ShowFields();
        UpdateStatus();
    }
    protected override bool ProcessCmdKey(ref Message msg,Keys keyData)
    {
        if(keyData==(Keys.Control|Keys.S)){if(!busy)_=SaveCurrent();return true;}
        if(keyData==(Keys.Control|Keys.Shift|Keys.S)){if(!busy)_=SaveAs();return true;}
        return base.ProcessCmdKey(ref msg,keyData);
    }
    async Task Open(string path)
    {
        if(busy || !grid.EndEdit() || !ConfirmDiscard())return;
        SetBusy(true,T("parsing"));
        try
        {
            var next=await Task.Run(()=>new BundleDocument(path));
            document?.Dispose();document=next;current=null;
            Text=T("app")+" v1.1 — "+Path.GetFileName(path);
            componentSearch.Clear();fieldSearch.Clear();FilterComponents();
            if(next.Components.Count==0)MessageBox.Show(this,T("noComponents"),T("parseResult"));
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,T("openFailed"),MessageBoxButtons.OK,MessageBoxIcon.Error);}
        finally{SetBusy(false);}
    }
    void FilterComponents()
    {
        if(populating || busy && document==null)return;
        var previous=current;populating=true;components.Items.Clear();
        if(document!=null)
            foreach(var c in document.Components.Where(c=>(kind.SelectedIndex==0 || c.Kind==(string)kind.SelectedItem!) && (c.Name+" "+c.Info.PathId).Contains(componentSearch.Text,StringComparison.OrdinalIgnoreCase)))components.Items.Add(c);
        if(previous!=null)components.SelectedItem=previous;
        if(components.SelectedIndex<0 && components.Items.Count>0)components.SelectedIndex=0;
        current=components.SelectedItem as Component;populating=false;ShowFields();
    }
    void ShowFields()
    {
        // Changing filters never discards an invalid cell silently.
        if(grid.IsCurrentCellInEditMode && !grid.EndEdit())return;
        populating=true;grid.Rows.Clear();
        if(current!=null)
        {
            title.Text=current.Kind+"  ·  "+current.Name;
            detail.Text=$"Path ID  {current.Info.PathId}   |   {current.File.name}";
            relation.Text=document!.Relation(current);
            foreach(var p in current.Parameters)
            {
                if(p.ReadOnly && !references.Checked)continue;
                string name=p.Field.FieldName;
                string label=L10n.Label(name);
                var descriptions=L10n.Labels.GetValueOrDefault(name);
                if(!(descriptions.Zh+" "+descriptions.En+" "+p.Path).Contains(fieldSearch.Text,StringComparison.OrdinalIgnoreCase))continue;
                int row=grid.Rows.Add(label,p.Path,p.Field.TypeName,p.Text,p.Original);
                var r=grid.Rows[row];r.Tag=p;r.Cells["value"].ReadOnly=p.ReadOnly;
                if(p.ReadOnly)r.DefaultCellStyle.ForeColor=Color.Gray;
                else if(p.Changed)r.Cells["value"].Style.BackColor=Color.FromArgb(255,238,185);
                else if(label!="")r.Cells["label"].Style.ForeColor=Color.FromArgb(22,97,125);
            }
        }
        else {title.Text=T("noMatch");detail.Text=document==null?T("intro"):T("adjustFilter");relation.Text="";}
        populating=false;UpdateStatus();
    }
    void ValidateCell(object? sender,DataGridViewCellValidatingEventArgs e)
    {
        if(populating || e.ColumnIndex!=grid.Columns["value"]!.Index || grid.Rows[e.RowIndex].Tag is not Parameter p || p.ReadOnly)return;
        try{p.Set(Convert.ToString(e.FormattedValue,CultureInfo.InvariantCulture)??"");grid.Rows[e.RowIndex].ErrorText="";grid.Rows[e.RowIndex].Cells["value"].Style.BackColor=p.Changed?Color.FromArgb(255,238,185):Color.Empty;}
        catch(Exception){e.Cancel=true;grid.Rows[e.RowIndex].ErrorText=T("invalid",T("invalidNumber"));status.Text=T("fixInput");}
    }
    void Reset()
    {
        if(current==null)return;grid.CancelEdit();
        foreach(var p in current.Parameters.Where(p=>!p.ReadOnly))p.Set(p.Original);
        ShowFields();RefreshComponentNames();
    }
    void RefreshComponentNames()
    {
        // ListBox caches display strings. Refresh each item while preserving selection.
        int index=components.SelectedIndex;populating=true;
        for(int i=0;i<components.Items.Count;i++)components.Items[i]=components.Items[i];
        components.SelectedIndex=index;populating=false;
    }
    async Task SaveCurrent()
    {
        if(document==null || busy || !grid.EndEdit() || document.ChangeCount==0)return;
        SetBusy(true,T("saving"));
        try
        {
            string? backup=await Task.Run(()=>document.Save());
            ShowFields();RefreshComponentNames();
            if(backup!=null)MessageBox.Show(this,T("savedInPlace",document.Source,backup),T("saved"));
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,T("saveFailed"),MessageBoxButtons.OK,MessageBoxIcon.Error);}
        finally{SetBusy(false);}
    }
    async Task SaveAs()
    {
        if(document==null || busy || !grid.EndEdit())return;
        using var dialog=new SaveFileDialog(){Filter=T("rfcFilter"),FileName=Path.GetFileNameWithoutExtension(document.Source)+"_edited.rfc",InitialDirectory=Path.GetDirectoryName(document.Source),OverwritePrompt=true};
        if(dialog.ShowDialog()!=DialogResult.OK)return;
        SetBusy(true,T("saving"));
        try
        {
            await Task.Run(()=>document.SaveAs(dialog.FileName));
            var next=await Task.Run(()=>new BundleDocument(dialog.FileName));
            document.Dispose();document=next;current=null;componentSearch.Clear();FilterComponents();
            Text=T("app")+" v1.1 — "+Path.GetFileName(document.Source);
            MessageBox.Show(this,T("savedAs",dialog.FileName),T("saved"));
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,T("saveFailed"),MessageBoxButtons.OK,MessageBoxIcon.Error);}
        finally{SetBusy(false);}
    }
    void SetBusy(bool value,string? message=null)
    {
        busy=value;UseWaitCursor=value;grid.Enabled=!value;components.Enabled=!value;componentSearch.Enabled=!value;kind.Enabled=!value;fieldSearch.Enabled=!value;references.Enabled=!value;language.Enabled=!value;open.Enabled=!value;log.Enabled=!value;
        UpdateStatus();if(message!=null)status.Text=message;
    }
    void UpdateStatus()
    {
        save.Enabled=document!=null && !busy;saveCurrent.Enabled=document!=null && (document.ChangeCount>0 || grid.IsCurrentCellDirty) && !busy;reset.Enabled=current!=null && !busy;
        if(busy)return;
        status.Text=document==null?T("ready"):T("status",document.UnityVersion,document.Components.Count,document.ChangeCount);
    }
    protected override void Dispose(bool disposing){if(disposing)document?.Dispose();base.Dispose(disposing);}
}

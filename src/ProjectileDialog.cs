namespace RfcEditor;

public sealed class ProjectileDialog : Form
{
    readonly CheckedListBox weapons=new(){Dock=DockStyle.Fill,CheckOnClick=true,IntegralHeight=false};
    readonly TextBox nameBox=new(){Dock=DockStyle.Top,MaxLength=160};
    public string CopyName=>nameBox.Text.Trim();
    public IEnumerable<Component> SelectedWeapons=>weapons.CheckedItems.Cast<Component>();
    public ProjectileDialog(BundleDocument document,Component projectile,bool clone)
    {
        Text=L10n.T(clone?"cloneTitle":"assignTitle");Font=new Font("Microsoft YaHei UI",9);
        Width=650;Height=500;MinimumSize=new Size(520,380);StartPosition=FormStartPosition.CenterParent;ShowInTaskbar=false;
        var body=new Panel(){Dock=DockStyle.Fill,Padding=new Padding(16)};
        var header=new FlowLayoutPanel(){Dock=DockStyle.Top,AutoSize=true,FlowDirection=FlowDirection.TopDown,WrapContents=false,Padding=new Padding(0,0,0,12)};
        header.Controls.Add(new Label(){Text="Projectile · "+projectile.Name,AutoSize=true,Font=new Font(Font,FontStyle.Bold)});
        if(clone)
        {
            header.Controls.Add(new Label(){Text=L10n.T("cloneName"),AutoSize=true});
            nameBox.Width=560;string proposed=L10n.T("cloneDefault",projectile.Name),candidate=proposed;int suffix=2;
            while(document.Components.Any(c=>c.Entry==projectile.Entry && c.Kind=="Projectile" && c.Name==candidate))candidate=proposed+" "+suffix++;
            nameBox.Text=candidate;header.Controls.Add(nameBox);
        }
        header.Controls.Add(new Label(){Text=L10n.T("weaponChoices"),AutoSize=true,MaximumSize=new Size(560,0),Margin=new Padding(0,12,0,0)});
        foreach(var weapon in document.Components.Where(c=>c.Kind=="Weapon" && c.Entry==projectile.Entry))
            weapons.Items.Add(weapon,!clone && document.ProjectileFor(weapon)==projectile);
        var hint=new Label(){Text=L10n.T(clone?"cloneHint":"assignHint"),Dock=DockStyle.Bottom,AutoSize=true,MaximumSize=new Size(580,0),Padding=new Padding(0,10,0,8)};
        body.Controls.Add(weapons);body.Controls.Add(header);body.Controls.Add(hint);
        var buttons=new FlowLayoutPanel(){Dock=DockStyle.Bottom,Height=48,FlowDirection=FlowDirection.RightToLeft,Padding=new Padding(8)};
        var cancel=new Button(){Text=L10n.T("cancel"),DialogResult=DialogResult.Cancel,AutoSize=true};
        var ok=new Button(){Text=L10n.T("ok"),AutoSize=true};
        ok.Click+=(_,_)=>{if(clone && CopyName.Length==0){MessageBox.Show(this,L10n.T("cloneNameInvalid"),Text);return;}DialogResult=DialogResult.OK;};
        buttons.Controls.Add(cancel);buttons.Controls.Add(ok);Controls.Add(body);Controls.Add(buttons);AcceptButton=ok;CancelButton=cancel;
    }
}

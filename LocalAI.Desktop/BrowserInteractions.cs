using System.IO;
using System.Text.Json;
using LocalAI.Core;

namespace LocalAI.Desktop;
internal sealed record BrowserActionPlan(string Id,string Action,string Value,string Url,long Generation,int Index,string Descriptor,string FormValues,string Review);
internal sealed partial class BrowserWindow
{
    private long agentDocument;
    private readonly Dictionary<string,(int Index,string Descriptor,string Label,string Type)> agentElements=[];
    private async Task WaitAgentReady(CancellationToken token=default)
    {
        for(var i=0;i<100&&(Current?.CoreWebView2==null||!readyViews.Contains(Current)||loadingViews.Contains(Current));i++)await Task.Delay(100,token);
        if(Current?.CoreWebView2==null||!readyViews.Contains(Current)||loadingViews.Contains(Current))throw new IOException("Browser page is still loading. Try again after it finishes.");
    }
    private void InvalidateAgentElements(){agentElements.Clear();agentDocument++;}
    // Fixed scripts only. No selector or script text is accepted from a model.
    private const string DescribeElement="""
const all=Array.from(document.querySelectorAll('input,textarea,select,button,a,[role=button]'));
const allowed=e=>{const t=(e.type||e.tagName).toLowerCase(); const s=[e.name,e.id,e.autocomplete,e.getAttribute('aria-label'),e.innerText].join(' ');return e.outerHTML.length<32000&&e.getClientRects().length&&!e.disabled&&!e.readOnly&&!['password','file','hidden'].includes(t)&&!/(password|passwd|secret|token|credit|cc-number|cc-csc|one.time|otp|login|sign.in|payment|checkout|purchase|buy.now)/i.test(s)&&!(e.form&&e.form.elements.length>100)&&!(e.form&&e.form.querySelector('input[type=password],input[type=file],[autocomplete^="cc-"],[autocomplete="one-time-code"]'));};
const descriptor=e=>JSON.stringify({html:e.outerHTML,form:e.form?{action:e.form.action,method:e.form.method,target:e.form.target}:null});
const formValues=e=>e.form?JSON.stringify(Array.from(e.form.elements).map(x=>({name:x.name,type:x.type,value:(x.value||''),checked:x.checked})).slice(0,100)):'';
""";
    internal async Task<string> AgentElements()
    {
        await WaitAgentReady();
        var core=Current?.CoreWebView2??throw new IOException("Open a browser page first.");var url=BrowserAddress.Normalize(core.Source);var generation=agentDocument;
        var raw=await core.ExecuteScriptAsync("(()=>{"+DescribeElement+"return JSON.stringify(all.map((e,index)=>({index,label:(e.getAttribute('aria-label')||e.labels?.[0]?.innerText||e.innerText||e.name||e.id||e.tagName).slice(0,120),type:(e.type||e.tagName).toLowerCase(),descriptor:descriptor(e),options:e.tagName==='SELECT'?Array.from(e.options).slice(0,40).map(o=>({value:o.value.slice(0,80),label:o.text.slice(0,80)})):[],ok:allowed(e)})).filter(x=>x.ok).slice(0,100));})()");
        if(core.Source!=url||generation!=agentDocument)throw new IOException("Page changed. List elements again.");
        using var doc=JsonDocument.Parse(JsonSerializer.Deserialize<string>(raw)??"[]");agentElements.Clear();var result=new List<object>();
        foreach(var e in doc.RootElement.EnumerateArray()){var id=Guid.NewGuid().ToString("N");var index=e.GetProperty("index").GetInt32();var descriptor=e.GetProperty("descriptor").GetString()!;var label=e.GetProperty("label").GetString()!;var type=e.GetProperty("type").GetString()!;agentElements[id]=(index,descriptor,label,type);result.Add(new{id,label,type,options=e.GetProperty("options").Clone()});}
        return JsonSerializer.Serialize(result);
    }
    internal async Task<BrowserActionPlan> PreviewAgentAction(string action,string id,string value)
    {
        if(action is not ("fill" or "click" or "submit"))throw new IOException("Unsupported browser interaction.");
        if(value.Length>4000||value.Contains('\0'))throw new IOException("Browser text is limited to 4,000 characters.");
        if(!agentElements.TryGetValue(id,out var target))throw new IOException("Element is missing or stale. List browser elements again.");
        if(action=="fill"&&target.Type is not ("text" or "search" or "email" or "url" or "tel" or "number" or "textarea" or "select-one"))throw new IOException("This element cannot be filled.");
        var core=Current?.CoreWebView2??throw new IOException("Browser not ready.");var url=BrowserAddress.Normalize(core.Source);var generation=agentDocument;
        var raw=await core.ExecuteScriptAsync("(()=>{"+DescribeElement+"const e=all["+target.Index+"];return JSON.stringify(e&&allowed(e)?{descriptor:descriptor(e),values:formValues(e),action:e.form?.action,method:e.form?.method,hasForm:!!e.form}:{});})()");
        using var doc=JsonDocument.Parse(JsonSerializer.Deserialize<string>(raw)??"{}");var data=doc.RootElement;
        if(generation!=agentDocument||url!=core.Source||!data.TryGetProperty("descriptor",out var d)||d.GetString()!=target.Descriptor)throw new IOException("Element changed. List elements again.");
        var destination=data.TryGetProperty("action",out var a)&&a.ValueKind==JsonValueKind.String?BrowserAddress.Normalize(a.GetString()!):url;
        if(action=="submit"&&!data.GetProperty("hasForm").GetBoolean())throw new IOException("Choose an element belonging to a form.");
        var values=data.GetProperty("values").GetString()??"";if(values.Length>16000)throw new IOException("This form exceeds the 16,000-character review limit. Use the browser manually.");var displayed=values;if(values.Length>0){using var fields=JsonDocument.Parse(values);displayed=JsonSerializer.Serialize(fields.RootElement.EnumerateArray().Select(f=>new{name=f.GetProperty("name").GetString(),type=f.GetProperty("type").GetString(),value=f.GetProperty("type").GetString()=="hidden"?"[hidden value retained locally for stale-state verification]":f.GetProperty("value").GetString(),isChecked=f.TryGetProperty("checked",out var checkedField)&&checkedField.ValueKind==JsonValueKind.True}));}
        return new(id,action,value,url,generation,target.Index,target.Descriptor,values,$"PAGE: {url}\nACTION: {action}\nELEMENT: {target.Label} ({target.Type})\nTEXT TO ENTER: {value}\nFORM DESTINATION: {destination}\nCURRENT FORM VALUES: {displayed}\n\nFilling and clicking may immediately run website scripts and transmit data. Submit may send the form using this browser's cookies. Review the destination and data. This receipt only confirms the browser action, not the site's business outcome.");
    }
    internal async Task<string> ApplyAgentAction(BrowserActionPlan plan,bool confirmed)
    {
        if(!confirmed)throw new IOException("Review and explicit confirmation are required.");var fresh=await PreviewAgentAction(plan.Action,plan.Id,plan.Value);
        if(fresh!=plan)throw new IOException("Page, element or form values changed after review. Nothing applied.");
        var core=Current!.CoreWebView2;var js="(()=>{"+DescribeElement+"const e=all["+plan.Index+"];if(!e||!allowed(e)||descriptor(e)!=="+JsonSerializer.Serialize(plan.Descriptor)+"||formValues(e)!=="+JsonSerializer.Serialize(plan.FormValues)+")return 'stale';";
        js+=plan.Action switch
        {
            "fill"=>"const v="+JsonSerializer.Serialize(plan.Value)+";if(e.tagName==='SELECT'){if(!Array.from(e.options).some(o=>o.value===v))return 'missing option';e.value=v;}else{const proto=e.tagName==='TEXTAREA'?HTMLTextAreaElement.prototype:HTMLInputElement.prototype;Object.getOwnPropertyDescriptor(proto,'value').set.call(e,v);}e.dispatchEvent(new Event('input',{bubbles:true}));e.dispatchEvent(new Event('change',{bubbles:true}));return 'filled';",
            "submit"=>"if(!e.form||!e.form.reportValidity())return 'invalid form';const f=e.form;if((e.tagName==='BUTTON'||e.tagName==='INPUT')&&e.type==='submit')f.requestSubmit(e);else f.requestSubmit();return 'submission requested';",
            _=>"e.click();return 'click requested';"
        };
        var result=JsonSerializer.Deserialize<string>(await core.ExecuteScriptAsync(js+"})()"))??"unknown";InvalidateAgentElements();
        if(result is "stale" or "missing option" or "invalid form" or "unknown")throw new IOException("Browser action was not completed: "+result);return result+" on "+plan.Url+". Verify the resulting page before claiming success.";
    }
}

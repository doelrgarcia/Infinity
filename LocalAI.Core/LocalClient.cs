using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace LocalAI.Core;
public sealed record InferenceResult(string Text, double TtftSeconds, double WallSeconds, int? PromptTokens, int? OutputTokens, double? DecodeTps, double? PrefillTps,string? FinishReason=null);
public sealed class LocalClient : IDisposable
{
    private const string DefaultHybridSystem="You are a helpful assistant. Answer the user's request directly. Do not invent additional conversation turns.";
    private readonly HttpClient http = new(new HttpClientHandler { AllowAutoRedirect=false, UseProxy=false }) { Timeout=Timeout.InfiniteTimeSpan };
    public static Uri ValidateEndpoint(string endpoint)
    {
        if(!Uri.TryCreate(endpoint,UriKind.Absolute,out var uri) || uri.Scheme!="http" || uri.Host!="127.0.0.1" || uri.AbsolutePath!="/" || uri.UserInfo!="" || uri.Query!="" || uri.Fragment!="")
            throw new InvalidDataException("Inference endpoint must be http://127.0.0.1:PORT with no path, credentials or query.");
        return uri;
    }
    public async Task<bool> Healthy(ModelProfile model, CancellationToken token)
    {
        try {
            var root=ValidateEndpoint(model.Endpoint);
            using var r=await http.GetAsync(new Uri(root,model.Engine=="ryzen"?"health":"v1/models"),token);
            if(!r.IsSuccessStatusCode)return false;
            using var json=JsonDocument.Parse(await r.Content.ReadAsStringAsync(token));
            if(model.Engine=="ryzen")
            {
                var actual=json.RootElement.GetProperty("model_path").GetString();
                if(!string.Equals(Path.GetFullPath(actual??""),Path.GetFullPath(model.ModelPath),StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Local port is serving a different model. Choose another port in appsettings.json.");
                if(json.RootElement.GetProperty("execution_mode").GetString()!="hybrid") throw new InvalidDataException("Ryzen runtime is not reporting the configured hybrid mode.");
                if(HybridBackend.Required(model))
                {
                    using var marker=await http.GetAsync(new Uri(root,"infinity/backend"),token);if(!marker.IsSuccessStatusCode)throw new InvalidDataException("This hybrid server lacks Infinity's streaming correction. Stop that older server yourself or choose another port.");
                    using var backend=JsonDocument.Parse(await marker.Content.ReadAsStringAsync(token));if(backend.RootElement.GetProperty("patch").GetString()!="sequence-growth-v2")throw new InvalidDataException("Hybrid streaming helper version differs from this app.");
                }
            }
            else
            {
                if(!json.RootElement.GetProperty("data").EnumerateArray().Any(x=>x.GetProperty("id").GetString()==model.Id)) throw new InvalidDataException("Local port is serving a different model. Choose another port in appsettings.json.");
                using var properties=await http.GetAsync(new Uri(root,"props"),token);properties.EnsureSuccessStatusCode();
                using var details=JsonDocument.Parse(await properties.Content.ReadAsStringAsync(token));var actual=details.RootElement.GetProperty("model_path").GetString();
                if(!string.Equals(Path.GetFullPath(actual??""),Path.GetFullPath(model.ModelPath),StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Local port model path differs from appsettings.json. Stop that runtime yourself or choose a different port.");
            }
            return true;
        }
        catch(HttpRequestException) { return false; }
    }
    public async Task<int> CountTokens(ModelProfile model, IReadOnlyList<Message> messages, CancellationToken token)
    {
        ImageInput.RequireProvider(model,messages);
        // For llama.cpp use its exact chat template followed by the model tokenizer.
        if(model.Engine!="llama") return checked(messages.Sum(m=>Encoding.UTF8.GetByteCount(FileAttachment.InputText(m))+32)+64+(model.Id=="llama32-hybrid"&&!messages.Any(m=>m.Role=="system")?Encoding.UTF8.GetByteCount(DefaultHybridSystem)+32:0));
        var root=ValidateEndpoint(model.Endpoint);
        using var template=await http.PostAsJsonAsync(new Uri(root,"apply-template"),new {messages=messages.Select(m=>new {role=m.Role,content=FileAttachment.InputText(m)}),add_generation_prompt=true},token);
        template.EnsureSuccessStatusCode();
        using var tj=JsonDocument.Parse(await template.Content.ReadAsStringAsync(token));
        var prompt=tj.RootElement.GetProperty("prompt").GetString();
        using var response=await http.PostAsJsonAsync(new Uri(root,"tokenize"),new {content=prompt,add_special=true},token);
        response.EnsureSuccessStatusCode();
        using var json=JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        return json.RootElement.GetProperty("tokens").GetArrayLength();
    }
    public async Task<InferenceResult> Generate(ModelProfile model, IReadOnlyList<Message> messages, int maxTokens, double temperature, Action<string> chunk, CancellationToken token,bool requireJson=false)
    {
        ImageInput.RequireProvider(model,messages);
        if(maxTokens<1 || maxTokens>=model.ContextLimit || temperature<0 || temperature>2 || !double.IsFinite(temperature)) throw new ArgumentOutOfRangeException(nameof(maxTokens));
        var count=await CountTokens(model,messages,token);
        if(count+maxTokens>model.ContextLimit) throw new InvalidOperationException($"Context exceeds {model.ContextLimit:N0} tokens: input {count:N0} + output reserve {maxTokens:N0}. Remove files/history or reduce output.");
        if(model.Engine=="ryzen"&&model.Id=="llama32-hybrid")return await GenerateLlama3(model,messages,maxTokens,temperature,chunk,token);
        var payload=new Dictionary<string,object>{{"model",model.Id},{"messages",messages.Select(m=>new{role=m.Role,content=FileAttachment.InputText(m)})},{"stream",true},{"stream_options",new{include_usage=true}},{"max_tokens",maxTokens},{"temperature",temperature}};
        if(requireJson&&model.Engine=="llama")payload["response_format"]=new{type="json_object",schema=AgentProtocol.ResponseSchema};
        using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(ValidateEndpoint(model.Endpoint),"v1/chat/completions")) {Content=JsonContent.Create(payload)};
        var watch=Stopwatch.StartNew(); double? first=null; int? input=null,output=null; double? decode=null,prefill=null;
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);
        if(!response.IsSuccessStatusCode) throw new HttpRequestException($"Local runtime HTTP {(int)response.StatusCode}. Check model path, context size and runtime logs.");
        using var reader=new StreamReader(await response.Content.ReadAsStreamAsync(token));
        var text=new StringBuilder(); bool completed=false;string? finishReason=null;
        while(await reader.ReadLineAsync(token) is { } line)
        {
            if(line.Length>1024*1024) throw new InvalidDataException("Runtime event exceeds 1 MiB.");
            if(!line.StartsWith("data:",StringComparison.Ordinal)) continue;
            var data=line[5..].Trim(); if(data=="[DONE]") { completed=true; break; }
            using var doc=JsonDocument.Parse(data); var root=doc.RootElement;
            if(root.TryGetProperty("error",out _)) throw new InvalidDataException("Runtime reported an inference error. Check runtime logs.");
            if(root.TryGetProperty("usage",out var usage) && usage.ValueKind==JsonValueKind.Object)
            {
                if(usage.TryGetProperty("prompt_tokens",out var p)) input=p.GetInt32();
                if(usage.TryGetProperty("completion_tokens",out var o)) output=o.GetInt32();
                if(usage.TryGetProperty("decoding_speed_tps",out var d)) decode=d.GetDouble();
            }
            if(root.TryGetProperty("timings",out var timings))
            {
                if(timings.TryGetProperty("predicted_per_second",out var d)) decode=d.GetDouble();
                if(timings.TryGetProperty("prompt_per_second",out var p)) prefill=p.GetDouble();
            }
            if(root.TryGetProperty("choices",out var choices)) foreach(var choice in choices.EnumerateArray())
            {
                if(choice.TryGetProperty("finish_reason",out var reason)&&reason.ValueKind==JsonValueKind.String)finishReason=reason.GetString();
                string? value=null;
                if(choice.TryGetProperty("delta",out var delta)&&delta.TryGetProperty("content",out var c)&&c.ValueKind==JsonValueKind.String)value=c.GetString();
                if(value is {Length:>0})
                {
                    first??=watch.Elapsed.TotalSeconds; text.Append(value); chunk(value);
                    if(text.Length>2_000_000) throw new InvalidDataException("Response exceeds 2 million characters.");
                }
            }
        }
        if(!completed) throw new EndOfStreamException("Runtime disconnected before completion; partial response retained.");
        return new(text.ToString(),first??watch.Elapsed.TotalSeconds,watch.Elapsed.TotalSeconds,input,output,decode,prefill,finishReason);
    }
    private static string Llama3Prompt(IReadOnlyList<Message> messages)
    {
        var prompt=new StringBuilder("<|begin_of_text|>");if(!messages.Any(m=>m.Role=="system"))prompt.Append("<|start_header_id|>system<|end_header_id|>\n\n").Append(DefaultHybridSystem).Append("<|eot_id|>");foreach(var message in messages){if(message.Role is not ("system" or "user" or "assistant"))throw new InvalidDataException("Unsupported local chat role.");prompt.Append("<|start_header_id|>").Append(message.Role).Append("<|end_header_id|>\n\n").Append(FileAttachment.InputText(message)).Append("<|eot_id|>");}return prompt.Append("<|start_header_id|>assistant<|end_header_id|>\n\n").ToString();
    }
    private async Task<InferenceResult> GenerateLlama3(ModelProfile model,IReadOnlyList<Message> messages,int maximum,double temperature,Action<string> chunk,CancellationToken token)
    {
        // Pinned Ryzen server 1.7 falls back to plain User/Assistant text when its
        // OGA template parser rejects this model's Jinja, and raw completions double
        // flush short output. Responses streams accept the correct raw Llama frame
        // without either fallback; final snapshots must agree with streamed deltas.
        using var request=new HttpRequestMessage(HttpMethod.Post,new Uri(ValidateEndpoint(model.Endpoint),"v1/responses")){Content=JsonContent.Create(new {model=model.Id,input=Llama3Prompt(messages),stream=true,max_output_tokens=maximum,temperature})};
        var watch=Stopwatch.StartNew();double? first=null;var text=new StringBuilder();var pending=new StringBuilder();var stopped=false;var final=false;var done=false;string? finishReason=null;var stops=new[]{"<|eot_id|>","<|end_of_text|>","<|eom_id|>"};
        void Emit(string value){if(value.Length==0)return;first??=watch.Elapsed.TotalSeconds;text.Append(value);if(text.Length>2_000_000)throw new InvalidDataException("Response exceeds 2 million characters.");chunk(value);}
        int StopIndex(string value)=>stops.Select(s=>value.IndexOf(s,StringComparison.Ordinal)).Where(i=>i>=0).DefaultIfEmpty(-1).Min();
        void Delta(string value)
        {
            if(stopped)return;if(text.Length+pending.Length+value.Length>2_000_000)throw new InvalidDataException("Response exceeds 2 million characters.");pending.Append(value);var buffer=pending.ToString();var stop=StopIndex(buffer);
            if(stop>=0){Emit(buffer[..stop]);pending.Clear();stopped=true;return;}
            var hold=0;foreach(var marker in stops)for(var size=1;size<marker.Length&&size<=buffer.Length;size++)if(buffer.EndsWith(marker[..size],StringComparison.Ordinal))hold=Math.Max(hold,size);
            Emit(buffer[..(buffer.Length-hold)]);pending.Clear();pending.Append(buffer.AsSpan(buffer.Length-hold));
        }
        using var response=await http.SendAsync(request,HttpCompletionOption.ResponseHeadersRead,token);if(!response.IsSuccessStatusCode)throw new HttpRequestException($"Local runtime HTTP {(int)response.StatusCode}. Check runtime logs.");
        using var reader=new StreamReader(await response.Content.ReadAsStreamAsync(token));
        while(await reader.ReadLineAsync(token) is { } line)
        {
            if(!line.StartsWith("data: ",StringComparison.Ordinal))continue;var data=line[6..];if(data=="[DONE]"){done=true;break;}
            using var doc=JsonDocument.Parse(data);var root=doc.RootElement;if(root.TryGetProperty("error",out _))throw new InvalidDataException("Hybrid runtime returned an error.");var type=root.TryGetProperty("type",out var kind)?kind.GetString():null;
            if(type is "response.failed" or "response.incomplete")throw new InvalidDataException("Hybrid runtime did not complete its response.");
            if(type=="response.output_text.delta"){if(final)throw new InvalidDataException("Hybrid runtime sent text after completion.");Delta(root.GetProperty("delta").GetString()??"");}
            if(type=="response.completed")
            {
                if(final)throw new InvalidDataException("Hybrid runtime completed twice.");
                var completedResponse=root.GetProperty("response");finishReason=completedResponse.GetProperty("finish_reason").GetString();if(finishReason is not ("stop" or "length"))throw new InvalidDataException("Hybrid runtime omitted its completion reason; reinstall the complete Infinity package.");
                var snapshot=string.Concat(root.GetProperty("response").GetProperty("output").EnumerateArray().SelectMany(x=>x.GetProperty("content").EnumerateArray()).Where(x=>x.GetProperty("type").GetString()=="output_text").Select(x=>x.GetProperty("text").GetString()));var stop=StopIndex(snapshot);if(stop>=0)snapshot=snapshot[..stop];
                Emit(pending.ToString());pending.Clear();if(snapshot!=text.ToString())throw new InvalidDataException("Hybrid final snapshot differs from streamed text.");final=true;
            }
        }
        if(!done||!final)throw new EndOfStreamException("Hybrid runtime disconnected before completed response; partial text retained.");
        return new(text.ToString(),first??watch.Elapsed.TotalSeconds,watch.Elapsed.TotalSeconds,null,null,null,null,finishReason);
    }
    public void Dispose()=>http.Dispose();
}

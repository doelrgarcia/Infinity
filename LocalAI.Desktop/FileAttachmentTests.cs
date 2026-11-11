using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using LocalAI.Core;
namespace LocalAI.Desktop;
public sealed partial class MainWindow
{
    private async Task ExerciseFileAttachments(Dictionary<string,object> rows)
    {
        var root=Path.Combine(Path.GetDirectoryName(smokeOutput!)!,"upload-fixtures");Directory.CreateDirectory(root);var text=Path.Combine(root,"Notes.txt");File.WriteAllText(text,"ORIGINAL_SNAPSHOT café");var docx=Path.Combine(root,"Brief.docx");
        using(var zip=ZipFile.Open(docx,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("word/document.xml").Open());writer.Write("<w:document xmlns:w='http://schemas.openxmlformats.org/wordprocessingml/2006/main'><w:body><w:p><w:r><w:t>Word document text</w:t></w:r></w:p></w:body></w:document>");}
        var pdf=Path.Combine(root,"Report.pdf");var objects=new[]{"<< /Type /Catalog /Pages 2 0 R >>","<< /Type /Pages /Kids [3 0 R] /Count 1 >>","<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 5 0 R >> >> /Contents 4 0 R >>","<< /Length 51 >>\nstream\nBT /F1 12 Tf 72 720 Td (Readable PDF fixture) Tj ET\nendstream","<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"};var builder=new StringBuilder("%PDF-1.4\n");var offsets=new List<int>();for(var i=0;i<objects.Length;i++){offsets.Add(builder.Length);builder.Append(i+1).Append(" 0 obj\n").Append(objects[i]).Append("\nendobj\n");}var xref=builder.Length;builder.Append("xref\n0 6\n0000000000 65535 f \n");foreach(var offset in offsets)builder.Append(offset.ToString("D10")).Append(" 00000 n \n");builder.Append("trailer\n<< /Size 6 /Root 1 0 R >>\nstartxref\n").Append(xref).Append("\n%%EOF");File.WriteAllText(pdf,builder.ToString(),Encoding.ASCII);
        rows["uploadsExtractActualPdfAndWordTextLocally"]=FileAttachment.Read(docx).Text.Contains("Word document text")&&FileAttachment.Read(pdf).Text.Contains("Readable PDF fixture");
        bool Refuses(string path){try{FileAttachment.Read(path);return false;}catch{return true;}}
        var privateFile=Path.Combine(root,"private.txt");File.WriteAllText(privateFile,"api_key=synthetic-fixture-key");var binary=Path.Combine(root,"binary.exe");File.WriteAllText(binary,"not executable");var empty=Path.Combine(root,"empty.txt");File.WriteAllText(empty,"");rows["uploadsRejectSecretsUnsupportedFilesAndEmptyDocuments"]=Refuses(privateFile)&&Refuses(binary)&&Refuses(empty);
        var hostile=Path.Combine(root,"Hostile.docx");using(var zip=ZipFile.Open(hostile,ZipArchiveMode.Create)){using var writer=new StreamWriter(zip.CreateEntry("word/document.xml").Open());writer.Write("<!DOCTYPE document [<!ENTITY leak SYSTEM 'file:///unread'>]><document>&leak;</document>");}rows["wordUploadsRejectDtdAndExternalEntities"]=Refuses(hostile);
        NewConversation();AttachPaths([text,docx]);var snapshot=DraftFiles[0];File.WriteAllText(text,"CHANGED_AFTER_ATTACHMENT");rows["documentSnapshotsRemainExactAfterOriginalFileChanges"]=snapshot.Text=="ORIGINAL_SNAPSHOT café"&&DraftFiles.Count==2;
        var chat=conversation;NewConversation();var separate=DraftFiles.Count==0;history.SelectedItem=chat;rows["attachmentDraftsAreSeparatedAndRestoredAcrossChats"]=separate&&DraftFiles.Count==2;
        Descendants<Button>(imageTray).First(b=>b.Content?.ToString()?.Contains("Notes.txt")==true).RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));rows["documentAttachmentPreviewOpensOnRightWithoutSending"]=documentText.Text==snapshot.Text&&rightPaneMode=="Document"&&conversation.Messages.Count==0;
        var before=DraftFiles.Count;try{AttachPaths([pdf,binary]);}catch{}rows["invalidMixedUploadBatchLeavesPriorAttachmentsUntouched"]=DraftFiles.Count==before&&DraftFiles[0]==snapshot;
        var bitmap=BitmapSource.Create(2,2,96,96,System.Windows.Media.PixelFormats.Bgra32,null,new byte[16],8);var png=Path.Combine(root,"Photo.png");var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(png))encoder.Save(stream);AttachPaths([png]);rows["mixedDocumentsAndImagesShowSeparatePreviewAndRemoveControls"]=DraftImages.Count==1&&DraftFiles.Count==2&&Descendants<Image>(imageTray).Any()&&Descendants<Button>(imageTray).Count(b=>Equals(b.Content,"Remove"))==3;
        var cloud=settings.Models.First(p=>p.Engine=="codex");var executable=cloud.Executable;cloud.Executable=Environment.ProcessPath!;model.SelectedItem=cloud;prompt.Text="Inspect attached files";var reviewed=false;
        try{_=Dispatcher.BeginInvoke(()=>{var review=Application.Current.Windows.OfType<ProviderConsent>().Single();reviewed=Descendants<TextBox>(review).Any(t=>t.Text.Contains("ORIGINAL_SNAPSHOT")&&t.Text.Contains("Word document text"))&&Descendants<Image>(review).Any();review.Close();});await Send();rows["cloudAttachmentReviewShowsExactTextAndImagesAndCancelPreservesDraft"]=reviewed&&conversation.Messages.Count==0&&DraftFiles.Count==2&&DraftImages.Count==1&&prompt.Text=="Inspect attached files";}
        finally{cloud.Executable=executable;}
        DraftImages.Clear();RefreshImageTray();var listener=new HttpListener();var socket=new TcpListener(IPAddress.Loopback,0);socket.Start();var port=((IPEndPoint)socket.LocalEndpoint).Port;socket.Stop();listener.Prefixes.Add($"http://127.0.0.1:{port}/");listener.Start();using var stopped=new CancellationTokenSource();var packets=new List<string>();
        var serving=Task.Run(async()=>{while(!stopped.IsCancellationRequested){HttpListenerContext c;try{c=await listener.GetContextAsync().WaitAsync(stopped.Token);}catch(Exception e) when(e is OperationCanceledException or HttpListenerException or ObjectDisposedException){break;}try{string output;if(c.Request.Url!.AbsolutePath=="/health")output=JsonSerializer.Serialize(new{model_path=root,execution_mode="hybrid"});else{using var reader=new StreamReader(c.Request.InputStream);var body=await reader.ReadToEndAsync();lock(packets)packets.Add(body);output="data: "+JsonSerializer.Serialize(new{choices=new[]{new{delta=new{content="{\"action\":\"reply\",\"value\":\"Fixture document answer\"}"},finish_reason="stop"}}})+"\n\ndata: [DONE]\n\n";}await c.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes(output));c.Response.Close();}catch(IOException){}}});
        var local=new ModelProfile{Id="document-loopback",Engine="ryzen",Label="Document loopback",ModelPath=root,Endpoint=$"http://127.0.0.1:{port}",ContextLimit=131072};settings.Models.Add(local);model.Items.Refresh();model.SelectedItem=local;
        try
        {
            await Send();var saved=store.Load(out _).Single(c=>c.Id==conversation.Id);rows["actualLocalRequestAndReloadContainExactDocumentSnapshots"]=packets.Last().Contains("ORIGINAL_SNAPSHOT")&&!packets.Last().Contains("CHANGED_AFTER_ATTACHMENT")&&saved.Messages.First(m=>m.Role=="user").Files?.Count==2&&DraftFiles.Count==0;
            rows["cliAndHybridPromptFormatsIncludeExtractedDocuments"]=CliAssistant.FormatPrompt(saved.Messages).Contains("ORIGINAL_SNAPSHOT")&&((string)typeof(LocalClient).GetMethod("Llama3Prompt",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic)!.Invoke(null,[saved.Messages])!).Contains("ORIGINAL_SNAPSHOT");
            OpenAgent();SaveAgentDestination(null,"");agentView!.Refresh();agentView.AddAttachments([docx]);await agentView.TestSend("Inspect document",local.Id);rows["agentAttachmentReachesRealInferenceAndSavedHistory"]=packets.Last().Contains("Word document text")&&Agent.Messages.Last(m=>m.Role=="user").Files?.Single().Name=="Brief.docx"&&agentView.TestAttachmentCounts.Files==0;
            agentView.AddAttachments([png]);var requests=packets.Count;await agentView.TestSend("Inspect image",local.Id);rows["unsupportedAgentImageProviderRetainsAttachmentWithoutTransmission"]=agentView.TestAttachmentCounts.Images==1&&packets.Count==requests;agentView.CloseForAttachmentTest();
            ShowChat();NewConversation();model.SelectedItem=local;AttachPaths([docx]);var task=StartChatGoal(new(){Objective="Inspect uploaded document",SuccessCriteria="Explain its contents",CompletionMode="User acceptance"},false);rows["goalTaskReceivesUploadedSnapshotWithoutLosingIt"]=task.Messages.Last().Files?.Single().Name=="Brief.docx"&&DraftFiles.Count==0&&task.Status=="Paused";
            var file=FileAttachment.Read(docx);var count=task.Messages.Count;task.Status="Running";try{AgentFollowUp(task,"Inspect this upload",files:[file]);rows["liveTaskUploadRefusesUntilSafeIdleBoundary"]=false;}catch(IOException){rows["liveTaskUploadRefusesUntilSafeIdleBoundary"]=task.Messages.Count==count&&task.FollowUps.Count==0;}finally{task.Status="Paused";SaveAgent();}
            NewConversation();AttachPaths([docx,png]);UpdateLayout();Capture(this,Path.ChangeExtension(smokeOutput!,".uploads.png"));DraftFiles.Clear();DraftImages.Clear();RefreshImageTray();
        }
        finally{stopped.Cancel();listener.Stop();await serving;listener.Close();}
    }
}
internal sealed partial class AgentView
{
    internal void CloseForAttachmentTest(){AttachmentDraft.Images.Clear();AttachmentDraft.Files.Clear();RefreshAttachments();}
}

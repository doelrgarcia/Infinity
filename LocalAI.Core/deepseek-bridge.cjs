// Embed only the installed Harness account/LLM components. No Agent, tool registry,
// workspace discovery, profile patches, telemetry, or command executor is mounted.
const path = require('node:path');
const { createRequire } = require('node:module');
const { pathToFileURL } = require('node:url');

function messagesFromPacket(packet) {
  if(!Array.isArray(packet)||packet.length===0||packet.some(m=>!m||!['system','user','assistant'].includes(m.role)||typeof m.content!=='string'))throw Error('INVALID_INPUT');
  return packet.map(m=>({role:m.role,content:[{type:'text',text:m.content}],
    // Harness requires assistant provenance even when no provider replay state is
    // carried. Infinity history may originate from any configured model.
    ...(m.role==='assistant'?{source:{kind:'model',provider:'infinity-text-history',model:'unknown'}}:{})}));
}

async function streamAnswer(llm, messages, emit, signal) {
  let finished = false, size = 0;
  for await (const chunk of llm.stream({provider:'deepseek-account', model:'deepseek-flash', messages,
      tools:[], maxTokens:4096, signal})) {
    if (chunk.type === 'tool-call-delta' || chunk.blockType === 'tool-call' || chunk.block?.type === 'tool-call')
      throw Error('TOOL_BLOCKED');
    if (chunk.type === 'text-delta') {
      if (typeof chunk.text !== 'string' || (size += chunk.text.length) > 1000000) throw Error('OUTPUT_LIMIT');
      emit({type:'text',text:chunk.text});
    }
    if (chunk.type === 'finish') {
      if (!['stop','max-tokens'].includes(chunk.reason?.kind)) throw Error(chunk.reason?.failure?.code || 'PROVIDER_FAILED');
      emit({type:'final',finish:chunk.reason.kind}); finished = true;
    }
  }
  if (!finished) throw Error('INCOMPLETE_STREAM');
}

async function createContext(bundle,home,probe=false) {
  const resolver=createRequire(path.join(bundle,'dsh','package.json'));
  const load=name=>import(pathToFileURL(resolver.resolve('@deepseek-ai/'+name)).href);
  const {Context}=await load('cordis');
  const llm=await load('dsh-llm');
  const credentials=await load('dsh-credentials-local');
  const authorization=await load('dsh-authorization');
  const account=await load('dsh-deepseek-account-platform');
  const provider=await load('dsh-llm-deepseek-account');
  const ctx=new Context();
  try {
    await ctx.plugin(llm.default);
    if(!probe) {
      const filename=path.join(home,'.credentials.yaml');
      let document;try{document=await require('node:fs/promises').readFile(filename,'utf8');}catch{throw Error('ACCOUNT_SIGN_IN_REQUIRED');}
      // Refuse legacy/invalid stores before the provider could migrate them.
      credentials.parseCredentialsDocument(document,filename);
      await ctx.plugin(credentials.default,{path:filename,watch:false});
      await ctx.plugin(authorization.default);
      await ctx.plugin(account.default,{inferenceOrigin:'https://api.deepseek.com',desktopPlatform:'win32'});
      await ctx.plugin(provider,{baseURL:'https://api.deepseek.com/anthropic',modelOverrides:{}});
      if(!ctx.deepseekAccount)throw Error('UNSAFE_COMPOSITION');
    }
    if(ctx.get('tools')||ctx.get('agents'))throw Error('UNSAFE_COMPOSITION');
    return ctx;
  } catch(e) {await ctx.fiber.dispose();throw e;}
}
async function main() {
  const [bundle,home,mode]=process.argv.slice(2);
  if(!['probe','generate'].includes(mode))throw Error('INVALID_MODE');
  console.info=()=>{};console.warn=()=>{};
  const ctx=await createContext(bundle,home,mode==='probe');
  try {
    if(mode==='probe'){process.stdout.write(JSON.stringify({bridgeReady:true,toolsAbsent:!ctx.get('tools')})+'\n');return;}
    process.stdin.setEncoding('utf8');let input='';for await(const part of process.stdin){input+=part;if(input.length>2097152)throw Error('INPUT_LIMIT');}
    const packet=JSON.parse(input);
    const messages=messagesFromPacket(packet);
    await streamAnswer(ctx.llm,messages,event=>process.stdout.write(JSON.stringify(event)+'\n'),AbortSignal.timeout(600000));
  } finally {await ctx.fiber.dispose();}
}
module.exports={streamAnswer,createContext,messagesFromPacket};
if(require.main===module)main().catch(e=>{
  // Provider payloads and credentials must never enter errors shown in Infinity.
  const code=String(e?.code||e?.message||'PROVIDER_FAILED');
  const allowed=['ACCOUNT_SIGN_IN_REQUIRED','ACCOUNT_TOKEN_INVALID','ACCOUNT_QUOTA_EXCEEDED','QUOTA_EXCEEDED','TOOL_BLOCKED','OUTPUT_LIMIT','INCOMPLETE_STREAM','INPUT_LIMIT','INVALID_INPUT','INVALID_MODE','UNSAFE_COMPOSITION'];
  process.stdout.write(JSON.stringify({type:'error',code:allowed.includes(code)?code:'PROVIDER_FAILED'})+'\n');process.exitCode=1;
});

using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.Security.Cryptography;
public static class PclDynamicPatchBuilder {
 static AssemblyDefinition a;
 static ModuleDefinition mod;
 static IEnumerable<TypeDefinition> Types(TypeDefinition t) { yield return t; foreach(var n in t.NestedTypes) foreach(var x in Types(n)) yield return x; }
 static IEnumerable<TypeDefinition> AllTypes() { return mod.Types.SelectMany(Types); }
 static TypeDefinition T(string name) { return AllTypes().Single(t=>t.FullName==name); }
 static MethodDefinition M(string type,string name,int argc=-1) { return T(type).Methods.Single(m=>m.Name==name&&(argc<0||m.Parameters.Count==argc)); }
 static FieldDefinition F(string type,string name) { return T(type).Fields.Single(f=>f.Name==name); }
 static IEnumerable<Instruction> AllIL() { return AllTypes().SelectMany(t=>t.Methods).Where(m=>m.HasBody).SelectMany(m=>m.Body.Instructions); }
 static MethodReference Ref(string name,Func<MethodReference,bool> check=null) { var matches=AllIL().Select(i=>i.Operand as MethodReference).Where(m=>m!=null&&m.Name==name&&(check==null||check(m))).GroupBy(m=>m.FullName).Select(g=>g.First()).ToArray(); if(matches.Length!=1)throw new Exception("结构识别失败：引用 "+name+" 应唯一匹配，实际 "+matches.Length); return matches[0]; }
 static Instruction I(OpCode op) { return Instruction.Create(op); }
 static Instruction I(OpCode op, MethodReference x) { return Instruction.Create(op,x); }
 static Instruction I(OpCode op, FieldReference x) { return Instruction.Create(op,x); }
 static void ReplaceRange(MethodDefinition m, Instruction start,Instruction end,Instruction replacement) {
   var b=m.Body.Instructions; int s=b.IndexOf(start),e=b.IndexOf(end);
   start.OpCode=replacement.OpCode;start.Operand=replacement.Operand;
   for(int j=s+1;j<e;j++) { b[j].OpCode=OpCodes.Nop;b[j].Operand=null; }
 }
 static Instruction FindPackRestriction(MethodDefinition pack){
   var targets=new Dictionary<Instruction,int>();
   foreach(var i in pack.Body.Instructions){
     var t=i.Operand as Instruction;if(t!=null){int n;targets.TryGetValue(t,out n);targets[t]=n+1;}
     var ts=i.Operand as Instruction[];if(ts!=null)foreach(var x in ts){int n;targets.TryGetValue(x,out n);targets[x]=n+1;}
   }
   var candidates=pack.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ldstr&&i.Next!=null&&i.Next.OpCode==OpCodes.Newobj&&i.Next.Operand is MethodReference&&((MethodReference)i.Next.Operand).DeclaringType.FullName=="System.Exception"&&i.Next.Next!=null&&i.Next.Next.OpCode==OpCodes.Throw).ToArray();
   if(candidates.Length==0)throw new Exception("找不到整合包限制入口");
   var preferred=candidates.Where(i=>((string)i.Operand).Contains("法律法规")).ToArray();
   if(preferred.Length==1)return preferred[0];
   if(candidates.Length==1)return candidates[0];
   var scored=candidates.Select(c=>new{I=c,N=(targets.ContainsKey(c)?targets[c]:0)}).OrderByDescending(x=>x.N).ToArray();
   if(scored.Length>0&&scored[0].N>0&&(scored.Length==1||scored[0].N>scored[1].N))return scored[0].I;
   throw new Exception("整合包限制入口不唯一");
 }
 static void LongBranches(MethodDefinition m) {
   foreach(var i in m.Body.Instructions) {
     if(i.OpCode==OpCodes.Br_S)i.OpCode=OpCodes.Br;
     else if(i.OpCode==OpCodes.Brtrue_S)i.OpCode=OpCodes.Brtrue;
     else if(i.OpCode==OpCodes.Brfalse_S)i.OpCode=OpCodes.Brfalse;
     else if(i.OpCode==OpCodes.Beq_S)i.OpCode=OpCodes.Beq;
     else if(i.OpCode==OpCodes.Bne_Un_S)i.OpCode=OpCodes.Bne_Un;
     else if(i.OpCode==OpCodes.Leave_S)i.OpCode=OpCodes.Leave;
     else if(i.OpCode==OpCodes.Bge_S)i.OpCode=OpCodes.Bge;
     else if(i.OpCode==OpCodes.Bgt_S)i.OpCode=OpCodes.Bgt;
     else if(i.OpCode==OpCodes.Ble_S)i.OpCode=OpCodes.Ble;
     else if(i.OpCode==OpCodes.Blt_S)i.OpCode=OpCodes.Blt;
   }
 }
 public static string Build(string src,string dst) {
   var depDir=Path.Combine(Path.GetTempPath(),"PclPatchDeps-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(depDir);
   var resolver=new DefaultAssemblyResolver();resolver.AddSearchDirectory(depDir);
   resolver.AddSearchDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Microsoft.NET/Framework64/v4.0.30319"));
   a=AssemblyDefinition.ReadAssembly(src,new ReaderParameters{AssemblyResolver=resolver});mod=a.MainModule;
   try {
   PclStructureGuard.Before(a);
   foreach(var res in mod.Resources.OfType<EmbeddedResource>().Where(r=>r.Name.StartsWith("costura.")&&r.Name.EndsWith(".dll.compressed"))) {
     string name=res.Name.Substring(8).Replace(".compressed",""); if(Path.GetFileName(name)!=name||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new Exception("Unsafe embedded dependency name");
     using(var input=res.GetResourceStream())using(var zip=new System.IO.Compression.DeflateStream(input,System.IO.Compression.CompressionMode.Decompress))using(var output=File.Create(Path.Combine(depDir,name)))zip.CopyTo(output);
   }
   foreach(var res in mod.Resources.OfType<EmbeddedResource>().Where(r=>r.Name.EndsWith(".resources"))) {
     using(var rr=new System.Resources.ResourceReader(res.GetResourceStream())) {
       var en=rr.GetEnumerator();while(en.MoveNext()) {
         string kind;byte[] raw;rr.GetResourceData((string)en.Key,out kind,out raw);
         if(kind!="ResourceTypeCode.ByteArray"||raw.Length<6)continue;
         int count=BitConverter.ToInt32(raw,0);
         if(count<2||count!=raw.Length-4||raw[4]!=77||raw[5]!=90)continue;
         string name=(string)en.Key+".dll";
         if(Path.GetFileName(name)!=name||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new Exception("Unsafe embedded dependency name");
         byte[] bytes=new byte[count];Buffer.BlockCopy(raw,4,bytes,0,count);File.WriteAllBytes(Path.Combine(depDir,name),bytes);
       }
     }
   }

   F("PCL.ModSecret","RestrictMcDownload").Constant=false;
   var dl=M("PCL.PageDownloadInstall","LoaderInit");
   var loaderCtors=dl.Body.Instructions.Where(i=>i.OpCode==OpCodes.Newobj&&((MethodReference)i.Operand).DeclaringType.Name.StartsWith("LoaderTask")).ToArray();
   if(loaderCtors.Length!=1)throw new Exception("下载初始化入口不唯一");
   var loaderCtor=loaderCtors[0];
   var start=dl.Body.Instructions.SingleOrDefault(i=>i.OpCode==OpCodes.Ldstr&&(string)i.Operand=="关闭下载");
   if(start==null){start=loaderCtor.Previous;while(start!=null&&start.OpCode!=OpCodes.Ldstr)start=start.Previous;}
   if(start==null||dl.Body.Instructions.IndexOf(start)>=dl.Body.Instructions.IndexOf(loaderCtor))throw new Exception("找不到下载初始化入口");
   ReplaceRange(dl,start,loaderCtor.Next,I(OpCodes.Ldsfld,F("PCL.ModDownload","DlClientListLoader")));
   var modify=M("PCL.PageDownloadModify","LoadInstanceFinished");
   var card=modify.Body.Instructions.Single(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="get_CardMinecraft");
   int cardValue;
   if(!PclStructureGuard.TryLdcI4(card.Next,out cardValue))throw new Exception("Unexpected card visibility");
   card.Next.OpCode=OpCodes.Ldc_I4_0;card.Next.Operand=null;
   var pack=M("PCL.ModModpack","ModpackInstall",3);
   var restriction=FindPackRestriction(pack);
   var throwIns=restriction.Next.Next;
   if(throwIns.OpCode!=OpCodes.Throw)throw new Exception("Unexpected pack restriction");
   var il=pack.Body.GetILProcessor();
   var result=new VariableDefinition(pack.ReturnType);pack.Body.Variables.Add(result);
   var retLoad=I(OpCodes.Ldloc,result);var ret=I(OpCodes.Ret);
   var tail=new List<Instruction>();
   int[] cases={0,1,2,3,4,9};
   string[] installers={"InstallPackCurseForge","InstallPackHMCL","InstallPackMMC","InstallPackMCBBS","InstallPackModrinth","InstallPackLauncherPack"};
   var labels=cases.Select(_=>I(OpCodes.Nop)).ToArray();
   for(int n=0;n<cases.Length;n++) {
     tail.Add(I(OpCodes.Ldloc,PclStructureGuard.PackKind));
     tail.Add(Instruction.Create(OpCodes.Ldc_I4,cases[n]));
     tail.Add(Instruction.Create(OpCodes.Beq,labels[n]));
   }
   tail.Add(I(OpCodes.Ldarg_0));tail.Add(I(OpCodes.Ldloc,PclStructureGuard.Archive));
   tail.Add(I(OpCodes.Call,M("PCL.ModModpack","InstallPackCompress")));
   tail.Add(I(OpCodes.Stloc,result));tail.Add(Instruction.Create(OpCodes.Leave,retLoad));
   for(int n=0;n<cases.Length;n++) {
     tail.Add(labels[n]);tail.Add(I(OpCodes.Ldarg_0));tail.Add(I(OpCodes.Ldloc,PclStructureGuard.Archive));tail.Add(I(OpCodes.Ldloc,PclStructureGuard.BaseFolder));
     if(cases[n]==0||cases[n]==3||cases[n]==4)tail.Add(I(OpCodes.Ldarg_1));
     if(cases[n]==0||cases[n]==4)tail.Add(I(OpCodes.Ldarg_2));
     tail.Add(I(OpCodes.Call,M("PCL.ModModpack",installers[n])));
     tail.Add(I(OpCodes.Stloc,result));tail.Add(Instruction.Create(OpCodes.Leave,retLoad));
   }
   string originalRestrictionText=(string)restriction.Operand;
   restriction.OpCode=OpCodes.Br;restriction.Operand=tail[0];
   il.InsertAfter(restriction,Instruction.Create(OpCodes.Ldstr,originalRestrictionText));
   var packAnchor=throwIns.Next;
   foreach(var x in tail)il.InsertBefore(packAnchor,x);
   il.Append(retLoad);il.Append(ret);
   foreach(var eh in pack.Body.ExceptionHandlers)if(eh.HandlerEnd==null)eh.HandlerEnd=retLoad;
   LongBranches(pack);
   var picker=M("PCL.ModModpack","ModpackInstall",0);
   var closure=PclStructureGuard.PickerClosure;
   var selectFile=Ref("SelectFile");
   var java=M("PCL.ModJava","ManuallyImportJava");
   var selectIns=java.Body.Instructions.Single(i=>i.Operand==selectFile || (i.Operand is MethodReference && ((MethodReference)i.Operand).FullName==selectFile.FullName));
   var nullableCtor=(MethodReference)selectIns.Previous.Operand;
   var implicitString=(MethodReference)selectIns.Previous.Previous.Operand;
   var first=Ref("FirstOrDefault",r=>r is GenericInstanceMethod&&((GenericInstanceMethod)r).GenericArguments.Count==1&&((GenericInstanceMethod)r).GenericArguments[0].MetadataType==MetadataType.String&&r.Parameters.Count==1);
   var empty=Ref("IsNullOrEmpty");
   var thread=M("PCL.ModBase","RunInThread");
   var actionCtor=Ref(".ctor",r=>r.DeclaringType.FullName=="System.Action");
   var originalPickerInstructions=picker.Body.Instructions.ToArray();
   picker.Body=new MethodBody(picker);picker.Body.InitLocals=true;
   var cv=new VariableDefinition(closure);picker.Body.Variables.Add(cv);
   var pi=picker.Body.GetILProcessor();var pickerRet=I(OpCodes.Ret);
   pi.Append(I(OpCodes.Newobj,closure.Methods.Single(m=>m.IsConstructor)));pi.Append(I(OpCodes.Stloc,cv));
   pi.Append(I(OpCodes.Ldloc,cv));
   pi.Append(Instruction.Create(OpCodes.Ldstr,"选择整合包压缩文件"));pi.Append(I(OpCodes.Ldc_I4_0));pi.Append(I(OpCodes.Ldnull));
   pi.Append(Instruction.Create(OpCodes.Ldstr,"整合包文件|*.rar;*.zip;*.mrpack"));
   pi.Append(I(OpCodes.Call,implicitString));pi.Append(I(OpCodes.Newobj,nullableCtor));pi.Append(I(OpCodes.Call,selectFile));pi.Append(I(OpCodes.Call,first));
   var fileField=PclStructureGuard.PickerFile;
   pi.Append(I(OpCodes.Stfld,fileField));pi.Append(I(OpCodes.Ldloc,cv));pi.Append(I(OpCodes.Ldfld,fileField));
   pi.Append(I(OpCodes.Call,empty));pi.Append(Instruction.Create(OpCodes.Brtrue,pickerRet));
   pi.Append(I(OpCodes.Ldloc,cv));pi.Append(I(OpCodes.Ldftn,PclStructureGuard.PickerWorker));pi.Append(I(OpCodes.Newobj,actionCtor));
   foreach(var param in thread.Parameters.Skip(1)) {
     if(param.ParameterType.MetadataType==MetadataType.String)pi.Append(param.Constant==null?I(OpCodes.Ldnull):Instruction.Create(OpCodes.Ldstr,(string)param.Constant));
     else if(param.ParameterType.MetadataType==MetadataType.Boolean||param.ParameterType.MetadataType==MetadataType.Int32||param.ParameterType.IsValueType)pi.Append(Instruction.Create(OpCodes.Ldc_I4,Convert.ToInt32(param.Constant)));
     else throw new Exception("Unsupported thread default: "+param);
   }
   pi.Append(I(OpCodes.Call,thread));pi.Append(pickerRet);
   foreach(var oldInstruction in originalPickerInstructions)pi.Append(oldInstruction);
   var details=M("PCL.PageDownloadResourceDetail","InitializeStackResourceVersions");
   var listCall=details.Body.Instructions.Single(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="ToListItem");
   var nullSave=listCall.Previous.Previous;
   if(nullSave.OpCode!=OpCodes.Ldnull)throw new Exception("Unexpected ToListItem args");
   var saveType=((MethodReference)listCall.Operand).Parameters[1].ParameterType;
   var saveDelegateCtor=Ref(".ctor",r=>r.DeclaringType.FullName==saveType.FullName);
   var installType=((MethodReference)listCall.Operand).Parameters[0].ParameterType;
   var installCtor=Ref(".ctor",r=>r.DeclaringType.FullName==installType.FullName);
   var oldSaveTarget=nullSave.Previous.Previous.Previous;
   if(oldSaveTarget.OpCode!=OpCodes.Ldarg_0)throw new Exception("Unexpected Save_Click target");
   var di=details.Body.GetILProcessor();var makeSave=I(OpCodes.Ldarg_0);var noInstall=I(OpCodes.Ldnull);
   var newInst=new List<Instruction>{
     I(OpCodes.Ldarg_0),I(OpCodes.Ldfld,F("PCL.PageDownloadResourceDetail","TargetResourceType")),I(OpCodes.Ldc_I4_2),
     Instruction.Create(OpCodes.Bne_Un,noInstall),I(OpCodes.Ldarg_0),I(OpCodes.Ldftn,M("PCL.PageDownloadResourceDetail","Install_Click")),I(OpCodes.Newobj,installCtor),
     Instruction.Create(OpCodes.Br,makeSave),noInstall,makeSave,I(OpCodes.Ldftn,M("PCL.PageDownloadResourceDetail","Save_Click")),I(OpCodes.Newobj,saveDelegateCtor)
   };
   ReplaceRange(details,oldSaveTarget,nullSave.Next,I(OpCodes.Nop));
   var detailAnchor=nullSave.Next;
   foreach(var x in newInst)di.InsertBefore(detailAnchor,x);
   LongBranches(details);
   a.Write(dst); PclStructureGuard.After(dst);
   return null;
   } finally { a.Dispose(); resolver.Dispose(); try { Directory.Delete(depDir,true); } catch { } }
 }
 public static PclPatchInfo Verify(string src) {
    var info=new PclPatchInfo();
    var resolver=new DefaultAssemblyResolver();
    resolver.AddSearchDirectory(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows),"Microsoft.NET/Framework64/v4.0.30319"));
    try {
      using(var a=AssemblyDefinition.ReadAssembly(src,new ReaderParameters{AssemblyResolver=resolver})) {
        PclStructureGuard.Inspect(a,info);
        try {
          var roles=PclStructureGuard.BindForVerify(a);
          info.Methods=roles.Keys.OrderBy(k=>k,StringComparer.Ordinal).ToArray();
          info.Supported=true;
        } catch(Exception ex) {
          info.Methods=new string[0];
          info.Supported=false;
          info.Error=ex.Message;
        }
      }
    } finally { resolver.Dispose(); }
    return info;
  }
 static Instruction I(OpCode op, VariableDefinition v) {return Instruction.Create(op,v);}
}
public sealed class PclPatchInfo {
  public string AssemblyName;
  public string AssemblyVersion;
  public string VersionBaseName;
  public int VersionCode;
  public string[] Methods;
  public bool Supported;
  public bool Patched;
  public string State;
  public int MarkerCount;
  public string[] Markers;
  public string Error;
}
public static class PclStructureGuard {
 public static TypeDefinition PickerClosure;
 public static MethodDefinition PickerWorker;
 public static FieldDefinition PickerFile;
 public static VariableDefinition PackKind,Archive,BaseFolder;
 static Dictionary<string,string> expectedBefore,expectedAfter;
 static Dictionary<string,string> originalMethods,originalFields;
 static Dictionary<string,byte[]> originalResources;
 static Dictionary<string,MethodDefinition> bound;
 static Dictionary<string,string> aliases;
 static string dragName,assemblyIdentity;
 public static bool BytesEqual(byte[] left,byte[] right){return left.SequenceEqual(right);}
 public static string HashFile(string path){using(var sha=SHA256.Create())using(var fs=File.OpenRead(path)){return BitConverter.ToString(sha.ComputeHash(fs)).Replace("-","").ToLowerInvariant();}}
 public static string[] BoundRoles(){Require(bound!=null,"尚未执行结构识别");return bound.Keys.OrderBy(k=>k,StringComparer.Ordinal).ToArray();}
 public static string GetConstantString(AssemblyDefinition a,string type,string field){var f=Unique(AllTypes(a).Where(t=>t.FullName==type),type).Fields.Single(x=>x.Name==field);Require(f.HasConstant&&f.Constant is string,type+"."+field+" 不是字符串常量");return (string)f.Constant;}
 public static int GetConstantInt(AssemblyDefinition a,string type,string field){var f=Unique(AllTypes(a).Where(t=>t.FullName==type),type).Fields.Single(x=>x.Name==field);Require(f.HasConstant,type+"."+field+" 不是常量");return Convert.ToInt32(f.Constant);}
 static FieldDefinition FieldOrNull(AssemblyDefinition a,string type,string name){var t=AllTypes(a).FirstOrDefault(x=>x.FullName==type);return t==null?null:t.Fields.FirstOrDefault(f=>f.Name==name);}
 static MethodDefinition MethodOrNull(AssemblyDefinition a,string type,string name,int argc){var t=AllTypes(a).FirstOrDefault(x=>x.FullName==type);if(t==null)return null;var ms=t.Methods.Where(m=>m.Name==name&&m.Parameters.Count==argc).ToArray();return ms.Length==1?ms[0]:null;}
 public static bool TryLdcI4(Instruction i,out int value){
    value=0;if(i==null)return false;
    if(i.OpCode.Code>=Code.Ldc_I4_M1&&i.OpCode.Code<=Code.Ldc_I4_8){value=(int)i.OpCode.Code-(int)Code.Ldc_I4_0;return true;}
    if(i.OpCode==OpCodes.Ldc_I4_S||i.OpCode==OpCodes.Ldc_I4){value=Convert.ToInt32(i.Operand);return true;}
    return false;
 }
 static bool Calls(MethodDefinition m,string name){return m!=null&&m.HasBody&&m.Body.Instructions.Any(i=>i.Operand is MethodReference&&((MethodReference)i.Operand).Name==name);}
 static bool HasFieldRef(MethodDefinition m,string type,string name){return m!=null&&m.HasBody&&m.Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).Name==name&&((FieldReference)i.Operand).DeclaringType.FullName==type);}
 static string[] DetectMarkers(AssemblyDefinition a){
   var markers=new List<string>();
   var download=MethodOrNull(a,"PCL.PageDownloadInstall","LoaderInit",0);
   if(HasFieldRef(download,"PCL.ModDownload","DlClientListLoader"))markers.Add("download");
   var modify=MethodOrNull(a,"PCL.PageDownloadModify","LoadInstanceFinished",1);
   if(modify!=null&&modify.HasBody){
     foreach(var i in modify.Body.Instructions){
       if(i.Operand is MethodReference&&((MethodReference)i.Operand).Name=="get_CardMinecraft"){
         int v;if(TryLdcI4(i.Next,out v)&&v==0){markers.Add("modify");break;}
       }
     }
   }
   var picker=MethodOrNull(a,"PCL.ModModpack","ModpackInstall",0);
   if(Calls(picker,"SelectFile"))markers.Add("pack-picker");
   var dispatch=MethodOrNull(a,"PCL.ModModpack","ModpackInstall",3);
   if(dispatch!=null){
     string[] names={"InstallPackCurseForge","InstallPackHMCL","InstallPackMMC","InstallPackMCBBS","InstallPackModrinth","InstallPackLauncherPack"};
     if(names.Count(n=>Calls(dispatch,n))==names.Length)markers.Add("pack-dispatch");
   }
   var details=MethodOrNull(a,"PCL.PageDownloadResourceDetail","InitializeStackResourceVersions",1);
   if(Calls(details,"Install_Click"))markers.Add("resource-list");
   return markers.ToArray();
 }
 public static void Inspect(AssemblyDefinition a,PclPatchInfo info){
   Require(a.Name.Name=="Plain Craft Launcher 2","程序集不是 PCL");
   Require(!a.Name.HasPublicKey&&(a.MainModule.Attributes&ModuleAttributes.StrongNameSigned)==0,"不支持需要重新签名的程序集");
   info.AssemblyName=a.Name.FullName;
   info.AssemblyVersion=a.Name.Version.ToString();
   try{info.VersionBaseName=GetConstantString(a,"PCL.ModBase","VersionBaseName");}catch{info.VersionBaseName="";}
   try{info.VersionCode=GetConstantInt(a,"PCL.ModBase","VersionCode");}catch{info.VersionCode=0;}
   var flag=FieldOrNull(a,"PCL.ModSecret","RestrictMcDownload");
   Require(flag!=null,"找不到下载限制开关 PCL.ModSecret.RestrictMcDownload");
   Require(flag.IsStatic&&flag.IsLiteral&&flag.FieldType.MetadataType==MetadataType.Boolean&&flag.HasConstant,"下载限制开关不是 Boolean 常量");
   info.Markers=DetectMarkers(a);
   info.MarkerCount=info.Markers.Length;
   bool off=flag.Constant is bool&&!(bool)flag.Constant;
   if(off){info.Patched=true;info.State=info.MarkerCount==5?"patched":"modified";}
   else if(info.MarkerCount>0){info.Patched=true;info.State="modified";}
   else {info.Patched=false;info.State="original";}
 }
 public static Dictionary<string,MethodDefinition> BindForVerify(AssemblyDefinition a){return Bind(a,false);}
 static IEnumerable<TypeDefinition> Types(TypeDefinition t) {yield return t;foreach(var n in t.NestedTypes)foreach(var x in Types(n))yield return x;}
 static IEnumerable<TypeDefinition> AllTypes(AssemblyDefinition a){return a.MainModule.Types.SelectMany(Types);}
 static IEnumerable<MethodDefinition> Methods(AssemblyDefinition a){return AllTypes(a).SelectMany(t=>t.Methods);}
 static void Require(bool ok,string why){if(!ok)throw new InvalidOperationException("结构识别失败："+why+"。未修改原文件。");}
 static T Unique<T>(IEnumerable<T> items,string what){var xs=items.ToArray();Require(xs.Length==1,what+" 应有 1 个匹配，实际 "+xs.Length);return xs[0];}
 static MethodDefinition Method(AssemblyDefinition a,string type,string name,int argc){return Unique(Unique(AllTypes(a).Where(t=>t.FullName==type),type).Methods.Where(m=>m.Name==name&&m.Parameters.Count==argc),type+"."+name);}
 static bool HasRestriction(MethodDefinition m){return m.HasBody&&m.Body.Instructions.Any(i=>i.OpCode==OpCodes.Ldstr&&((string)i.Operand).Contains("法律法规"));}
 static bool Call(Instruction i,string name){return i.Operand is MethodReference&&((MethodReference)i.Operand).Name==name;}
 static VariableDefinition Store(MethodDefinition m,Instruction i){
   if(i.OpCode==OpCodes.Stloc_0)return m.Body.Variables[0];if(i.OpCode==OpCodes.Stloc_1)return m.Body.Variables[1];
   if(i.OpCode==OpCodes.Stloc_2)return m.Body.Variables[2];if(i.OpCode==OpCodes.Stloc_3)return m.Body.Variables[3];
   Require(i.OpCode==OpCodes.Stloc||i.OpCode==OpCodes.Stloc_S,"局部变量赋值模式不符");return (VariableDefinition)i.Operand;
 }
 static Dictionary<string,MethodDefinition> Bind(AssemblyDefinition a,bool output){
   var r=new Dictionary<string,MethodDefinition>();
   r["download"]=Method(a,"PCL.PageDownloadInstall","LoaderInit",0);
   r["modify"]=Method(a,"PCL.PageDownloadModify","LoadInstanceFinished",1);
   r["pack-picker"]=Method(a,"PCL.ModModpack","ModpackInstall",0);
   r["pack-dispatch"]=Method(a,"PCL.ModModpack","ModpackInstall",3);
   r["resource-list"]=Method(a,"PCL.PageDownloadResourceDetail","InitializeStackResourceVersions",1);
   foreach(var m in r.Values)Require(m.HasBody,"目标方法没有 IL："+m.FullName);
   if(output)r["file-drag"]=Unique(Methods(a).Where(m=>m.FullName==dragName),"拖入整合包处理方法");
   else {
     r["file-drag"]=Unique(Methods(a).Where(m=>HasRestriction(m)&&m.Body.Instructions.Any(i=>Call(i,"get_Message"))),"限制异常专用 catch");
     dragName=r["file-drag"].FullName;
   }
   var ctor=Unique(r["pack-picker"].Body.Instructions.Where(i=>i.OpCode==OpCodes.Newobj&&i.Operand is MethodReference&&((MethodReference)i.Operand).DeclaringType.Scope==a.MainModule).GroupBy(i=>((MethodReference)i.Operand).FullName).Select(g=>g.First()),"整合包选择器闭包构造器");
   PickerClosure=Unique(AllTypes(a).Where(t=>t.FullName==((MethodReference)ctor.Operand).DeclaringType.FullName),"整合包选择器闭包");
   PickerWorker=Unique(PickerClosure.Methods.Where(m=>m.HasBody&&m.Body.Instructions.Any(i=>Call(i,"ModpackInstall")&&((MethodReference)i.Operand).Parameters.Count==3)),"原整合包后台安装闭包");
   PickerFile=Unique(PickerClosure.Fields.Where(f=>f.FieldType.MetadataType==MetadataType.String&&PickerWorker.Body.Instructions.Any(i=>i.Operand is FieldReference&&((FieldReference)i.Operand).FullName==f.FullName)),"闭包中的整合包路径字段");
   r["pack-worker"]=PickerWorker;
   aliases=new Dictionary<string,string>();
   aliases[PickerWorker.FullName]="$PACK_WORKER";
   aliases[PickerFile.FullName]="$PACK_FILE";
   aliases[PickerClosure.FullName]="$PACK_CLOSURE";
   aliases[r["file-drag"].DeclaringType.FullName]="$DRAG_CLOSURE";
   foreach(var m in Methods(a).Where(HasRestriction)){if(!r.Values.Contains(m))r["helper:"+m.FullName]=m;}
   return r;
 }
 static string Alias(string s){foreach(var pair in aliases.OrderByDescending(p=>p.Key.Length))s=s.Replace(pair.Key,pair.Value);return s;}
 static string TypeName(TypeReference t){return Alias(t==null?"<null>":t.FullName);}
 static string Canon(MethodDefinition m){
   if(!m.HasBody)return "NO_BODY";
   var all=m.Body.Instructions;var active=all.Where(i=>i.OpCode!=OpCodes.Nop).ToList();
   Func<Instruction,string> ix=i=>i==null?"END":("@"+active.Count(x=>x.Offset<i.Offset));
   var lines=new List<string>();
   lines.Add("signature "+TypeName(m.ReturnType)+"("+string.Join(",",m.Parameters.Select(p=>TypeName(p.ParameterType)).ToArray())+") static="+m.IsStatic);
   lines.Add("locals init="+m.Body.InitLocals+" "+string.Join(",",m.Body.Variables.Select(v=>TypeName(v.VariableType)).ToArray()));
   foreach(var i in active){
     string op=i.OpCode.Name;object x=i.Operand;string val="";
     if(op.EndsWith(".s",StringComparison.Ordinal))op=op.Substring(0,op.Length-2);
     if(i.OpCode.Code>=Code.Ldc_I4_M1&&i.OpCode.Code<=Code.Ldc_I4_8){op="ldc.i4";val=((int)i.OpCode.Code-(int)Code.Ldc_I4_0).ToString();}
     else if(i.OpCode==OpCodes.Ldc_I4||i.OpCode==OpCodes.Ldc_I4_S){op="ldc.i4";val=Convert.ToString(x,System.Globalization.CultureInfo.InvariantCulture);}
     else if(i.OpCode.Code>=Code.Ldloc_0&&i.OpCode.Code<=Code.Ldloc_3){op="ldloc";val="V"+((int)i.OpCode.Code-(int)Code.Ldloc_0);}
     else if(i.OpCode.Code>=Code.Stloc_0&&i.OpCode.Code<=Code.Stloc_3){op="stloc";val="V"+((int)i.OpCode.Code-(int)Code.Stloc_0);}
     else if(x is Instruction)val=ix((Instruction)x);
     else if(x is Instruction[])val=string.Join(",",((Instruction[])x).Select(ix).ToArray());
     else if(x is VariableDefinition)val="V"+((VariableDefinition)x).Index;
     else if(x is ParameterDefinition)val="P"+((ParameterDefinition)x).Index;
     else if(x is TypeReference)val=TypeName((TypeReference)x);
     else if(x is MemberReference)val=Alias(((MemberReference)x).FullName);
     else if(x is string){string s=(string)x;if(s.EndsWith(".vb",StringComparison.OrdinalIgnoreCase)&&s.Contains(":\\"))s="$SOURCE_PATH";val="str:"+s.Replace("\\","\\\\").Replace("\r","\\r").Replace("\n","\\n");}
     else if(x!=null)val=Convert.ToString(x,System.Globalization.CultureInfo.InvariantCulture);
     lines.Add(op+" "+val);
   }
   foreach(var e in m.Body.ExceptionHandlers)lines.Add("EH "+e.HandlerType+" "+ix(e.TryStart)+" "+ix(e.TryEnd)+" "+ix(e.HandlerStart)+" "+ix(e.HandlerEnd)+" "+ix(e.FilterStart)+" "+TypeName(e.CatchType));
   return string.Join("\n",lines.ToArray());
 }
 static void Match(Dictionary<string,string> expected,Dictionary<string,MethodDefinition> actual,string stage){
   foreach(var p in expected){Require(actual.ContainsKey(p.Key),"找不到 "+p.Key);string observed=Canon(actual[p.Key]);if(observed!=p.Value){
     var want=p.Value.Split('\n');var got=observed.Split('\n');int n=0;while(n<Math.Min(want.Length,got.Length)&&want[n]==got[n])n++;
     throw new InvalidOperationException("结构识别失败 ["+stage+"/"+p.Key+"] 第 "+(n+1)+" 项：\n预期："+(n<want.Length?want[n]:"<结束>")+"\n实际："+(n<got.Length?got[n]:"<结束>")+"\n未修改原文件。");
   }}
 }
 public static void SetReference(string xml){
   var doc=new System.Xml.XmlDocument();doc.XmlResolver=null;doc.LoadXml(xml);
   expectedBefore=new Dictionary<string,string>();expectedAfter=new Dictionary<string,string>();
   foreach(System.Xml.XmlElement e in doc.SelectNodes("/pcl/input/method"))expectedBefore.Add(e.GetAttribute("id"),e.InnerText);
   foreach(System.Xml.XmlElement e in doc.SelectNodes("/pcl/output/method"))expectedAfter.Add(e.GetAttribute("id"),e.InnerText);
   Require(expectedBefore.Count>=8&&expectedAfter.Count==expectedBefore.Count,"识别规则不完整");
 }
 static Dictionary<string,string> FieldMap(AssemblyDefinition a){return AllTypes(a).SelectMany(t=>t.Fields).ToDictionary(f=>f.FullName,f=>f.Attributes+"|"+f.HasConstant+"|"+Convert.ToString(f.Constant,System.Globalization.CultureInfo.InvariantCulture));}
 public static void Before(AssemblyDefinition a){
   var state=new PclPatchInfo();Inspect(a,state);
   Require(!state.Patched,"目标已处于修补/修改状态（"+state.State+"），请先还原");
   bound=Bind(a,false);
   var restrictions=Methods(a).Where(HasRestriction).ToArray();
   Require(restrictions.All(m=>bound.Values.Contains(m)),"出现未识别的额外限制入口");
   var pack=bound["pack-dispatch"];
   PackKind=Store(pack,Unique(pack.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ldc_I4_M1),"PackType 初始化").Next);
   Archive=Store(pack,Unique(pack.Body.Instructions.Where(i=>Call(i,"OpenZip")),"整合包 OpenZip 调用").Next);
   BaseFolder=Store(pack,Unique(pack.Body.Instructions.Where(i=>i.OpCode==OpCodes.Ldstr&&(string)i.Operand==""),"ArchiveBaseFolder 初始化").Next);
   Require(PackKind.VariableType.MetadataType==MetadataType.Int32&&Archive.VariableType.FullName=="System.IO.Compression.ZipArchive"&&BaseFolder.VariableType.MetadataType==MetadataType.String,"整合包局部变量类型不符");
   string ret=pack.ReturnType.FullName;
   var specs=new Dictionary<string,string[]>{
     {"InstallPackCurseForge",new[]{"System.String","System.IO.Compression.ZipArchive","System.String","System.String","System.String"}},
     {"InstallPackHMCL",new[]{"System.String","System.IO.Compression.ZipArchive","System.String"}},
     {"InstallPackMMC",new[]{"System.String","System.IO.Compression.ZipArchive","System.String"}},
     {"InstallPackMCBBS",new[]{"System.String","System.IO.Compression.ZipArchive","System.String","System.String"}},
     {"InstallPackModrinth",new[]{"System.String","System.IO.Compression.ZipArchive","System.String","System.String","System.String"}},
     {"InstallPackLauncherPack",new[]{"System.String","System.IO.Compression.ZipArchive","System.String"}},
     {"InstallPackCompress",new[]{"System.String","System.IO.Compression.ZipArchive"}}
   };
   foreach(var p in specs){var installer=Unique(pack.DeclaringType.Methods.Where(m=>m.Name==p.Key),p.Key);Require(installer.IsStatic&&installer.ReturnType.FullName==ret&&installer.Parameters.Select(x=>x.ParameterType.FullName).SequenceEqual(p.Value),p.Key+" 签名变化");}
   var loader=Unique(Unique(AllTypes(a).Where(t=>t.FullName=="PCL.ModDownload"),"ModDownload").Fields.Where(f=>f.Name=="DlClientListLoader"),"正常下载器字段");
   Require(loader.IsStatic&&loader.FieldType.FullName.Contains("LoaderTask`2<System.String,PCL.ModDownload/DlClientListResult>"),"正常下载器字段类型变化");
   assemblyIdentity=a.Name.FullName;
   originalMethods=Methods(a).ToDictionary(m=>m.FullName,Canon);
   originalFields=FieldMap(a);
   originalResources=a.MainModule.Resources.OfType<EmbeddedResource>().ToDictionary(r=>r.Name,r=>r.GetResourceData());
 }
 public static void After(string path){
   using(var a=AssemblyDefinition.ReadAssembly(path)){
     var current=Bind(a,true);
     var state=new PclPatchInfo();Inspect(a,state);
     Require(state.Patched&&state.MarkerCount==5,"输出未检测到完整的 5 处修补标记："+string.Join(",",state.Markers));
     Require(a.Name.FullName==assemblyIdentity,"输出程序集身份变化");
     var methods=Methods(a).ToDictionary(m=>m.FullName,Canon);
     Require(methods.Count==originalMethods.Count&&methods.Keys.All(originalMethods.ContainsKey),"输出方法集合变化");
     var edited=new HashSet<string>(current.Where(p=>new[]{"download","modify","pack-picker","pack-dispatch","resource-list"}.Contains(p.Key)).Select(p=>p.Value.FullName));
     foreach(var p in originalMethods)Require(edited.Contains(p.Key)||methods[p.Key]==p.Value,"非目标方法被修改："+p.Key);
     var fields=FieldMap(a);Require(fields.Count==originalFields.Count&&fields.Keys.All(originalFields.ContainsKey),"输出字段集合变化");
     foreach(var p in originalFields)if(!p.Key.EndsWith(" PCL.ModSecret::RestrictMcDownload",StringComparison.Ordinal))Require(fields[p.Key]==p.Value,"非目标字段被修改："+p.Key);
     var flag=AllTypes(a).Single(t=>t.FullName=="PCL.ModSecret").Fields.Single(f=>f.Name=="RestrictMcDownload");Require(flag.Constant is bool&&!(bool)flag.Constant,"输出开关未关闭");
     var res=a.MainModule.Resources.OfType<EmbeddedResource>().ToDictionary(r=>r.Name,r=>r.GetResourceData());
     Require(res.Count==originalResources.Count&&res.Keys.All(originalResources.ContainsKey),"内嵌资源集合变化");
     foreach(var p in originalResources)Require(p.Value.SequenceEqual(res[p.Key]),"内嵌资源被修改："+p.Key);
   }
 }
 public static string MakeReference(string input,string output){
   expectedBefore=null;expectedAfter=null;
   var sb=new System.Text.StringBuilder();
   using(var writer=System.Xml.XmlWriter.Create(sb,new System.Xml.XmlWriterSettings{Indent=true,OmitXmlDeclaration=true,NewLineChars="\n"})){
     writer.WriteStartElement("pcl");
     using(var a=AssemblyDefinition.ReadAssembly(input)){
       var inputRoles=Bind(a,false);expectedBefore=inputRoles.ToDictionary(p=>p.Key,p=>Canon(p.Value));
       writer.WriteStartElement("input");foreach(var p in expectedBefore){writer.WriteStartElement("method");writer.WriteAttributeString("id",p.Key);writer.WriteString(p.Value);writer.WriteEndElement();}writer.WriteEndElement();
     }
     using(var a=AssemblyDefinition.ReadAssembly(output)){
       var outputRoles=Bind(a,true);writer.WriteStartElement("output");foreach(var p in outputRoles){writer.WriteStartElement("method");writer.WriteAttributeString("id",p.Key);writer.WriteString(Canon(p.Value));writer.WriteEndElement();}writer.WriteEndElement();
     }
     writer.WriteEndElement();
   }
   return sb.ToString();
 }
}

// PCL2 CE is published as a .NET single-file bundle.  Keep this parser/rebuilder
// independent from Cecil so the bundle format can be validated before any IL is
// changed.  All offsets refer to the original file image.
public sealed class PclCeBundleFile {
 public long Offset;
 public long Size;
 public long CompressedSize;
 public byte Type;
 public string Name;
}

public sealed class PclCeBundle {
 static readonly byte[] BundleSignature = new byte[] {
  0x8b,0x12,0x02,0xb9,0x6a,0x61,0x20,0x38,0x72,0x7b,0x93,0x02,0x14,0xd7,0xa0,0x32,
  0x13,0xf5,0xb9,0xe6,0xef,0xae,0x33,0x18,0xee,0x3b,0x2d,0xce,0x24,0xb3,0x6a,0xae
 };
 public byte[] Raw;
 public long HeaderOffset;
 public uint MajorVersion;
 public uint MinorVersion;
 public string BundleId;
 public long DepsJsonOffset;
 public long DepsJsonSize;
 public long RuntimeConfigOffset;
 public long RuntimeConfigSize;
 public ulong Flags;
 public List<PclCeBundleFile> Files;
 public int SignatureOffset;
 public long DataOffset;

 static bool SignatureAt(byte[] raw,int offset) {
   if(offset<0||offset+BundleSignature.Length>raw.Length)return false;
   for(int i=0;i<BundleSignature.Length;i++)if(raw[offset+i]!=BundleSignature[i])return false;
   return true;
 }

 public static PclCeBundle Open(string path) {
   if(String.IsNullOrWhiteSpace(path))throw new ArgumentException("PCL2 CE bundle path is empty");
   return Parse(File.ReadAllBytes(path));
 }

 public static PclCeBundle Parse(byte[] raw) {
   if(raw==null||raw.Length<64)return null;
   for(int signature=8;signature<=raw.Length-BundleSignature.Length;signature++) {
     if(!SignatureAt(raw,signature))continue;
     long header=BitConverter.ToInt64(raw,signature-8);
     if(header<=0||header>=raw.Length-1)continue;
     try {
       var bundle=ParseAt(raw,header,signature);
       if(bundle!=null)return bundle;
     } catch { }
   }
   return null;
 }

 static PclCeBundle ParseAt(byte[] raw,long header,int signature) {
   var bundle=new PclCeBundle();
   bundle.Raw=raw;bundle.HeaderOffset=header;bundle.SignatureOffset=signature;
   bundle.Files=new List<PclCeBundleFile>();
   using(var ms=new MemoryStream(raw,false)) {
     ms.Position=header;
     using(var reader=new BinaryReader(ms,Encoding.UTF8)) {
       bundle.MajorVersion=reader.ReadUInt32();
       bundle.MinorVersion=reader.ReadUInt32();
       int count=reader.ReadInt32();
       if(bundle.MajorVersion<1||bundle.MajorVersion>6||bundle.MinorVersion!=0||count<0||count>4096)return null;
       bundle.BundleId=reader.ReadString();
       if(bundle.BundleId==null||bundle.BundleId.Length>256)return null;
       if(bundle.MajorVersion>=2) {
         bundle.DepsJsonOffset=reader.ReadInt64();
         bundle.DepsJsonSize=reader.ReadInt64();
         bundle.RuntimeConfigOffset=reader.ReadInt64();
         bundle.RuntimeConfigSize=reader.ReadInt64();
         bundle.Flags=reader.ReadUInt64();
       }
       long first=Int64.MaxValue;
       for(int i=0;i<count;i++) {
         var f=new PclCeBundleFile();
         f.Offset=reader.ReadInt64();
         f.Size=reader.ReadInt64();
         if(bundle.MajorVersion>=6)f.CompressedSize=reader.ReadInt64();
         f.Type=reader.ReadByte();
         f.Name=reader.ReadString();
         if(String.IsNullOrEmpty(f.Name)||f.Name.Length>2048||f.Offset<0||f.Size<0||f.CompressedSize<0)return null;
         long stored=f.CompressedSize>0?f.CompressedSize:f.Size;
         if(f.Offset>raw.Length||stored>raw.Length-f.Offset)return null;
         if(f.Offset<first)first=f.Offset;
         bundle.Files.Add(f);
       }
       if(bundle.Files.Count!=count||first==Int64.MaxValue||first<=signature||first>=header||header<=first)return null;
       bundle.DataOffset=first;
       return bundle;
     }
   }
 }

 public byte[] ReadFile(PclCeBundleFile file) {
   if(file==null)throw new ArgumentNullException("file");
   long stored=file.CompressedSize>0?file.CompressedSize:file.Size;
   if(file.Offset<0||file.Offset>Raw.Length||stored<0||stored>Raw.Length-file.Offset)throw new InvalidDataException("PCL2 CE bundle entry is outside the file: "+file.Name);
   if(file.Size>Int32.MaxValue||stored>Int32.MaxValue)throw new InvalidDataException("PCL2 CE bundle entry is too large: "+file.Name);
   if(file.CompressedSize>0) {
     using(var input=new MemoryStream(Raw,(int)file.Offset,(int)file.CompressedSize,false))
     using(var deflate=new DeflateStream(input,CompressionMode.Decompress))
     using(var output=new MemoryStream()) {
       deflate.CopyTo(output);
       byte[] result=output.ToArray();
       if(result.Length!=file.Size)throw new InvalidDataException("PCL2 CE bundle entry size mismatch: "+file.Name);
       return result;
     }
   }
   byte[] data=new byte[(int)file.Size];
   Buffer.BlockCopy(Raw,(int)file.Offset,data,0,data.Length);
   return data;
 }

 static void Align(Stream stream,int alignment) {
   while((stream.Position%alignment)!=0)stream.WriteByte(0);
 }

 public byte[] Rebuild(IDictionary<string,byte[]> replacements) {
   if(Files==null||Files.Count==0||DataOffset<=SignatureOffset||DataOffset>Raw.Length)throw new InvalidDataException("PCL2 CE bundle layout is invalid");
   var output=new MemoryStream();
   output.Write(Raw,0,(int)DataOffset);
   var newFiles=new List<PclCeBundleFile>();
   foreach(var file in Files) {
     byte[] data=null;
     if(replacements!=null)replacements.TryGetValue(file.Name,out data);
     if(data==null)data=ReadFile(file);
     Align(output,16);
     var copy=new PclCeBundleFile();
     copy.Offset=output.Position;copy.Size=data.Length;copy.CompressedSize=0;copy.Type=file.Type;copy.Name=file.Name;
     output.Write(data,0,data.Length);
     newFiles.Add(copy);
   }
   long depsOffset=0,depsSize=0,runtimeOffset=0,runtimeSize=0;
   foreach(var file in newFiles) {
     if(file.Type==3){depsOffset=file.Offset;depsSize=file.Size;}
     if(file.Type==4){runtimeOffset=file.Offset;runtimeSize=file.Size;}
   }
   long header=output.Position;
   using(var writer=new BinaryWriter(output,Encoding.UTF8,true)) {
     writer.Write(MajorVersion);
     writer.Write(MinorVersion);
     writer.Write(newFiles.Count);
     writer.Write(BundleId);
     if(MajorVersion>=2) {
       writer.Write(depsOffset);writer.Write(depsSize);
       writer.Write(runtimeOffset);writer.Write(runtimeSize);
       writer.Write(Flags);
     }
     foreach(var file in newFiles) {
       writer.Write(file.Offset);
       writer.Write(file.Size);
       if(MajorVersion>=6)writer.Write((long)0);
       writer.Write(file.Type);
       writer.Write(file.Name);
     }
     writer.Flush();
   }
   byte[] result=output.ToArray();
   if(SignatureOffset<8||SignatureOffset>result.Length)throw new InvalidDataException("PCL2 CE apphost signature is outside the output");
   byte[] pointer=BitConverter.GetBytes(header);
   Buffer.BlockCopy(pointer,0,result,SignatureOffset-8,8);
   return result;
 }
}

public sealed class PclCePatchInfo {
 public bool IsPclCe;
 public bool Supported;
 public bool Patched;
 public string State;
 public string Version;
 public string CoreAssembly;
 public string MainAssembly;
 public string Error;
 public string[] Markers;
 public string[] Targets;
}

public static class PclCePatchBuilder {
 const string MarkerTypeName="PCL.Core.App.Region.PclCePatchMarker";
 const string MarkerFieldName="Applied";
 const string MarkerValue="PCLCE-PATCH-OPT-1";

 static IEnumerable<TypeDefinition> Types(TypeDefinition type) {
   yield return type;
   foreach(var nested in type.NestedTypes)foreach(var child in Types(nested))yield return child;
 }
 static IEnumerable<TypeDefinition> AllTypes(AssemblyDefinition assembly) {
   return assembly.MainModule.Types.SelectMany(Types);
 }
 static TypeDefinition FindType(AssemblyDefinition assembly,string fullName) {
   foreach(var type in AllTypes(assembly))if(type.FullName==fullName)return type;
   return null;
 }
 static MethodDefinition FindMethod(TypeDefinition type,string name,int argc) {
   if(type==null)return null;
   foreach(var method in type.Methods)if(method.Name==name&&(argc<0||method.Parameters.Count==argc))return method;
   return null;
 }
 static bool HasType(AssemblyDefinition assembly,string fullName) {
   return FindType(assembly,fullName)!=null;
 }
 static AssemblyDefinition ReadAssembly(byte[] bytes) {
   return ReadAssembly(bytes,null);
 }
 static AssemblyDefinition ReadAssembly(byte[] bytes,string searchDirectory) {
   var parameters=new ReaderParameters{InMemory=true,ReadSymbols=false};
   var resolver=new DefaultAssemblyResolver();
   if(!String.IsNullOrWhiteSpace(searchDirectory))resolver.AddSearchDirectory(searchDirectory);
   string dotnetRoot=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),"dotnet","shared");
   if(Directory.Exists(dotnetRoot)) {
     foreach(var family in Directory.GetDirectories(dotnetRoot)) {
       foreach(var version in Directory.GetDirectories(family))resolver.AddSearchDirectory(version);
     }
   }
   parameters.AssemblyResolver=resolver;
   return AssemblyDefinition.ReadAssembly(new MemoryStream(bytes),parameters);
 }
 static byte[] WriteAssembly(AssemblyDefinition assembly) {
   using(var output=new MemoryStream()) {
     assembly.Write(output,new WriterParameters{WriteSymbols=false});
     return output.ToArray();
   }
 }
 static MethodDefinition IsRestrictedGetter(AssemblyDefinition core) {
   var type=FindType(core,"PCL.Core.App.Region.RegionAssessment");
   if(type==null)return null;
   foreach(var property in type.Properties)if(property.Name=="IsRestricted"&&property.GetMethod!=null)return property.GetMethod;
   return FindMethod(type,"get_IsRestricted",0);
 }
 static MethodDefinition FeatureCheck(AssemblyDefinition main) {
   var type=FindType(main,"PCL.RestrictedFeaturePolicy");
   return FindMethod(type,"Check",2);
 }
 static bool IsSimpleReturn(MethodDefinition method,bool expected) {
   if(method==null||method.Body==null)return false;
   var instructions=method.Body.Instructions;
   if(instructions.Count!=2||instructions[1].OpCode!=OpCodes.Ret)return false;
   if(expected)return instructions[0].OpCode==OpCodes.Ldc_I4_1;
   return instructions[0].OpCode==OpCodes.Ldc_I4_0;
 }
 static bool HasMarker(AssemblyDefinition core) {
   var type=FindType(core,MarkerTypeName);
   if(type==null)return false;
   foreach(var field in type.Fields)if(field.Name==MarkerFieldName&&field.HasConstant&&String.Equals(Convert.ToString(field.Constant),MarkerValue,StringComparison.Ordinal))return true;
   return false;
 }
 static void AddMarker(AssemblyDefinition core) {
   if(HasMarker(core))return;
   var type=new TypeDefinition("PCL.Core.App.Region","PclCePatchMarker",TypeAttributes.NotPublic|TypeAttributes.Abstract|TypeAttributes.Sealed|TypeAttributes.Class,core.MainModule.TypeSystem.Object);
   var field=new FieldDefinition(MarkerFieldName,FieldAttributes.Public|FieldAttributes.Static|FieldAttributes.Literal|FieldAttributes.HasDefault,core.MainModule.TypeSystem.String);
   field.Constant=MarkerValue;
   type.Fields.Add(field);
   core.MainModule.Types.Add(type);
 }
 static void SetReturn(MethodDefinition method,bool value) {
   method.Body=new MethodBody(method);
   var il=method.Body.GetILProcessor();
   il.Emit(value?OpCodes.Ldc_I4_1:OpCodes.Ldc_I4_0);
   il.Emit(OpCodes.Ret);
 }
 static void FindAssemblies(PclCeBundle bundle,out string coreName,out string mainName,out byte[] coreBytes,out byte[] mainBytes) {
   coreName=null;mainName=null;coreBytes=null;mainBytes=null;
   foreach(var file in bundle.Files) {
     if(!file.Name.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))continue;
     byte[] bytes;
     try{bytes=bundle.ReadFile(file);}catch{continue;}
     try {
       using(var assembly=ReadAssembly(bytes)) {
         if(coreName==null&&HasType(assembly,"PCL.Core.App.Region.RegionPolicy")){coreName=file.Name;coreBytes=bytes;}
         if(mainName==null&&HasType(assembly,"PCL.RestrictedFeaturePolicy")){mainName=file.Name;mainBytes=bytes;}
       }
     } catch { }
     if(coreName!=null&&mainName!=null)return;
   }
 }

 public static PclCePatchInfo Verify(string source) {
   var info=new PclCePatchInfo();
   info.Markers=new string[0];
   info.Targets=new string[0];
   try {
     var bundle=PclCeBundle.Open(source);
     if(bundle==null)return info;
     string coreName,mainName;byte[] coreBytes,mainBytes;
     FindAssemblies(bundle,out coreName,out mainName,out coreBytes,out mainBytes);
     if(coreName==null)return info;
     info.IsPclCe=true;info.CoreAssembly=coreName;info.MainAssembly=mainName;
     if(mainName==null) {info.Error="找不到 PCL.RestrictedFeaturePolicy";return info;}
     using(var core=ReadAssembly(coreBytes))
     using(var main=ReadAssembly(mainBytes)) {
       var getter=IsRestrictedGetter(core);
       var check=FeatureCheck(main);
       var marker=HasMarker(core);
       info.Supported=getter!=null&&check!=null;
       info.Version=(main.Name.Version==null?"":main.Name.Version.ToString())+" / "+(core.Name.Version==null?"":core.Name.Version.ToString());
       var targets=new List<string>();
       var markers=new List<string>();
       if(getter!=null)targets.Add("RegionAssessment.IsRestricted");
       if(check!=null)targets.Add("RestrictedFeaturePolicy.Check");
       bool corePatched=marker||IsSimpleReturn(getter,false);
       bool mainPatched=marker||IsSimpleReturn(check,true);
       if(marker)markers.Add("PclCePatchMarker");
       if(IsSimpleReturn(getter,false))markers.Add("RegionAssessment.IsRestricted=false");
       if(IsSimpleReturn(check,true))markers.Add("RestrictedFeaturePolicy.Check=true");
       info.Targets=targets.ToArray();
       info.Markers=markers.ToArray();
       if(!info.Supported) {info.Error="PCL2 CE 区域策略结构不受支持";return info;}
       if(corePatched&&mainPatched)info.State="patched";
       else if(corePatched||mainPatched)info.State="partial";
       else info.State="original";
       info.Patched=String.Equals(info.State,"patched",StringComparison.Ordinal);
     }
   } catch(Exception ex) {
     info.Error=ex.Message;
   }
   return info;
 }

 public static void Build(string source,string destination) {
   var bundle=PclCeBundle.Open(source);
   if(bundle==null)throw new InvalidDataException("不是可识别的 PCL2 CE single-file bundle");
   string coreName,mainName;byte[] coreBytes,mainBytes;
   FindAssemblies(bundle,out coreName,out mainName,out coreBytes,out mainBytes);
   if(coreName==null||mainName==null)throw new InvalidDataException("找不到 PCL2 CE 区域策略程序集");
   string searchDirectory=Path.Combine(Path.GetTempPath(),"PclCePatch-"+Guid.NewGuid().ToString("N"));
   Directory.CreateDirectory(searchDirectory);
   try {
     foreach(var file in bundle.Files) {
       if(!file.Name.EndsWith(".dll",StringComparison.OrdinalIgnoreCase))continue;
       string name=Path.GetFileName(file.Name);
       if(String.IsNullOrWhiteSpace(name)||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0)continue;
       string path=Path.Combine(searchDirectory,name);
       if(File.Exists(path))continue;
       try{File.WriteAllBytes(path,bundle.ReadFile(file));}catch{ }
     }
     byte[] patchedCore,patchedMain;
     using(var core=ReadAssembly(coreBytes,searchDirectory)) {
       var getter=IsRestrictedGetter(core);
       if(getter==null)throw new InvalidDataException("找不到 RegionAssessment.IsRestricted");
       SetReturn(getter,false);
       AddMarker(core);
       patchedCore=WriteAssembly(core);
     }
     using(var main=ReadAssembly(mainBytes,searchDirectory)) {
       var check=FeatureCheck(main);
       if(check==null)throw new InvalidDataException("找不到 RestrictedFeaturePolicy.Check");
       SetReturn(check,true);
       patchedMain=WriteAssembly(main);
     }
     var replacements=new Dictionary<string,byte[]>(StringComparer.OrdinalIgnoreCase);
     replacements[coreName]=patchedCore;
     replacements[mainName]=patchedMain;
     byte[] output=bundle.Rebuild(replacements);
     File.WriteAllBytes(destination,output);
   } finally {
     try{Directory.Delete(searchDirectory,true);}catch{ }
   }
 }
}
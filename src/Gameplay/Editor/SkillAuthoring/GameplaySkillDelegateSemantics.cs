using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Compares callback implementation, independently of compiler closure ordinals.
    /// Generated callbacks use their IL and resolved business members, not metadata tokens or
    /// lambda names. Captured values are compared separately by the definition comparator.</summary>
    internal static class GameplaySkillDelegateSemantics
    {
        private static readonly Dictionary<short,OpCode> Codes=typeof(OpCodes).GetFields(BindingFlags.Public|BindingFlags.Static)
            .Where(f=>f.FieldType==typeof(OpCode)).Select(f=>(OpCode)f.GetValue(null)).ToDictionary(c=>c.Value);
        private static readonly Dictionary<MethodInfo,string> Cache=new Dictionary<MethodInfo,string>();
        private static string Name(string name)
        {
            name=(name??"").Replace("FRFrame.Gameplay.Authoring.V2.","FRFrame.Gameplay.Authoring.Graph.");
            name=Regex.Replace(name, "(Gameplay(?:Unity|Spine|Live2D)?Animation|GameplayUnitySummon)(?:Asset|Data)","$1Values");
            name=Regex.Replace(name,@"<>9__\d+_\d+","<>9__");
            return Regex.Replace(name,@"<>c__DisplayClass\d+_\d+","<>c__DisplayClass");
        }
        internal static string Signature(MethodInfo method)=>Signature(method,new HashSet<MethodInfo>());
        private static string Signature(MethodInfo method,HashSet<MethodInfo> active)
        {
            if(Cache.TryGetValue(method,out var saved))return saved;
            if(!active.Add(method))return "recursive:"+Name(method.DeclaringType?.FullName)+":"+Name(method.ReturnType.FullName);
            try{
                bool generated=method.Name.Contains("<")||method.DeclaringType?.Name.Contains("<")==true;
                var signature=new StringBuilder(Name(method.DeclaringType?.FullName)).Append("::").Append(generated?"callback":method.Name)
                    .Append('(').Append(string.Join(",",method.GetParameters().Select(p=>Name(p.ParameterType.FullName)))).Append(")->").Append(Name(method.ReturnType.FullName));
                var body=method.GetMethodBody();
                if(body==null)throw new InvalidOperationException("Cannot prove callback implementation: "+method);
                foreach(var local in body.LocalVariables)signature.Append("local:").Append(Name(local.LocalType.FullName));
                var bytes=body.GetILAsByteArray();int cursor=0;
                while(cursor<bytes.Length){
                    short code=bytes[cursor++];if(code==0xfe)code=(short)(0xfe00|bytes[cursor++]);var op=Codes[code];
                    signature.Append('|').Append(op.Name).Append(':');
                    switch(op.OperandType){
                        case OperandType.InlineNone:break;
                        case OperandType.ShortInlineI:case OperandType.ShortInlineVar:case OperandType.ShortInlineBrTarget:signature.Append(bytes[cursor++]);break;
                        case OperandType.InlineVar:signature.Append(BitConverter.ToUInt16(bytes,cursor));cursor+=2;break;
                        case OperandType.InlineI8:case OperandType.InlineR:signature.Append(BitConverter.ToString(bytes,cursor,8));cursor+=8;break;
                        case OperandType.InlineSwitch:
                            int count=BitConverter.ToInt32(bytes,cursor);signature.Append(BitConverter.ToString(bytes,cursor,4+count*4));cursor+=4+count*4;break;
                        case OperandType.InlineField:case OperandType.InlineMethod:case OperandType.InlineType:case OperandType.InlineTok:
                            int token=BitConverter.ToInt32(bytes,cursor);cursor+=4;
                            var member=method.Module.ResolveMember(token,method.DeclaringType?.GetGenericArguments(),method.IsGenericMethod?method.GetGenericArguments():null);
                            var called=member as MethodInfo;
                            if(called!=null&&(called.Name.Contains("<")||called.DeclaringType?.Name.Contains("<")==true))signature.Append(Signature(called,active));
                            else signature.Append(Name(member.DeclaringType?.FullName)).Append("::").Append(Name(member.ToString()));
                            break;
                        case OperandType.InlineString:signature.Append(method.Module.ResolveString(BitConverter.ToInt32(bytes,cursor)));cursor+=4;break;
                        case OperandType.InlineSig:throw new InvalidOperationException("Unproven callback signature operand: "+method);
                        default:signature.Append(BitConverter.ToString(bytes,cursor,4));cursor+=4;break;
                    }
                }
                foreach(var clause in body.ExceptionHandlingClauses)signature.Append("exception:").Append(clause.Flags).Append(':').Append(clause.TryOffset).Append(':').Append(clause.TryLength).Append(':').Append(clause.HandlerOffset).Append(':').Append(clause.HandlerLength).Append(':').Append(clause.Flags==ExceptionHandlingClauseOptions.Clause?Name(clause.CatchType?.FullName):"");
                using(var sha=SHA256.Create())saved=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(signature.ToString())));
                Cache.Add(method,saved);return saved;
            }finally{active.Remove(method);}
        }
    }
}

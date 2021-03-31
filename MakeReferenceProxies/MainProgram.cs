
#region Usings
using AsmResolver.DotNet;
using AsmResolver.DotNet.Code.Cil;
using AsmResolver.DotNet.Signatures;
using AsmResolver.DotNet.Signatures.Types;
using AsmResolver.PE.DotNet.Cil;
using AsmResolver.PE.DotNet.Metadata.Tables.Rows;
using System;
using System.Collections.Generic;
using System.Linq;
#endregion

namespace MakeReferenceProxies
{
    /// <summary>
    /// Main Class.
    /// </summary>
    static class MainProgram
    {
        /// <summary>
        /// Module We Loaded on <c> Main() </c>.
        /// </summary>
        private static ModuleDefinition _module;
        /// <summary>
        /// Reference Importer From Module We Loaded.
        /// </summary>
        private static ReferenceImporter _importer;
        /// <summary>
        /// Storing Our Maded Methods For Reusing Them if its Same Call.
        /// </summary>
        private static Dictionary<IMethodDescriptor, MethodDefinition> Cache;
        /// <summary>
        /// EntryPoint method.
        /// </summary>
        static void Main()
        {
            // Assign Cache.
            Cache = new Dictionary<IMethodDescriptor, MethodDefinition>();
            // Ask For Path.
            Console.Write("[-] Path : ");
            // Get Module Path By User.
            var _path = Console.ReadLine().Replace("\"", "");
            // Load Module From Path provided.
            _module = ModuleDefinition.FromFile(_path);
            // Assign Importer.
            _importer = new ReferenceImporter(_module);
            // Starting Proxy Make.
            DoProxies();
            // Saving Module.
            _module.Write(_path.Insert(_path.Length - 4, "RefProxied"));
        }
        /// <summary>
        /// Do Proxy Progress.
        /// </summary>
        private static void DoProxies()
        {
            // Get All Types In Module (Note That GetAllTypes() Includes Nested Types).
            foreach (TypeDefinition TypeDef in _module.GetAllTypes().Where(Type => Type.Methods.Count > 0 && !Type.IsCompilerGenerated()).ToArray()) /* Purify Types Has No Methods & Not Compiler Gen. Using Linq. */ {
                foreach (MethodDefinition MethodDef in TypeDef.Methods.Where(Method => Method.HasMethodBody && !Method.IsNative).ToArray()) /* Purify Methods Has No CilBody & Not x86 Method Using Linq. */ {
                    // Expanding Method Instructions (Some Times Branches MayBreak so Expaning Is Required).
                    MethodDef.CilMethodBody.Instructions.ExpandMacros();
                    // Get IL_Codes from MethodDef CilBody.
                    var ILCode = MethodDef.CilMethodBody.Instructions;
                    // Loop in ILCodes to Manage Process
                    for (int x = 0; x < ILCode.Count; x++) {
                        // Check If Current Instruction is Vaild Call Instruction.
                        if (!(ILCode[x].OpCode == CilOpCodes.Call || ILCode[x].OpCode == CilOpCodes.Callvirt || ILCode[x].OpCode == CilOpCodes.Newobj)) continue;
                        // No Generic-Methods (You Can Do It But That Simple-Tutorial You Must Release you Ideas After Learning :D).
                        if (!(ILCode[x].Operand is not MethodSpecification)) continue;
                        // Assign Method In Variable.
                        var _calledmethod = (IMethodDescriptor)ILCode[x].Operand;
                        // Assign If That Instruction is Newobj.
                        bool IsNewObj = ILCode[x].OpCode == CilOpCodes.Newobj;
                        // Check Cache.
                        if (!Cache.ContainsKey(_calledmethod)) {
                            // Make New Method (Proxy-Method).
                            var _proxymethod = new MethodDefinition(Guid.NewGuid().GetHashCode().ToString("x"),
                                MethodAttributes.Static | MethodAttributes.Public | MethodAttributes.ReuseSlot,
                                GetSignature(IsNewObj, _calledmethod));
                            // Valided Proxy CilBody.
                            _proxymethod.ProcessProxyBody(ILCode[x]);
                            // Add Proxy-Meth to Current TypeDef.
                            TypeDef.Methods.Add(_proxymethod);
                            // Change OpCode to Call.
                            ILCode[x].OpCode = CilOpCodes.Call;
                            // Change Operand To Imported ProxyMethod.
                            ILCode[x].Operand = _importer.ImportMethod(_proxymethod);
                            // Add to cache.
                            Cache.Add(_calledmethod, _proxymethod);
                        }
                        else {
                            // Change OpCode to Call.
                            ILCode[x].OpCode = CilOpCodes.Call;
                            // Change Operand To Stored Method.
                            ILCode[x].Operand = Cache[_calledmethod];
                        }
                    }
                    // Serializing Instructions After Processing.
                    MethodDef.CilMethodBody.Instructions.OptimizeMacros();
                }
            }
        }
        /// <summary>
        /// Do Proxy Signature.
        /// </summary>
        /// <param name="IsNewobj"> Ditermine If The Processed Instruction Is CilOpCodes.NewObj. </param>
        /// <param name="Method"> Method to Call. </param>
        /// <returns> Proxy Method Sig. </returns>
        private static MethodSignature GetSignature(bool IsNewobj, IMethodDescriptor Method)
        {
            // Get Return Type.
            var _returntype = IsNewobj
                              ? Method.DeclaringType.ToTypeSignature()
                              : Method.Signature.ReturnType;
            // Assign Params Type.
            IList<TypeSignature> _params = new List<TypeSignature>();
            /// Inserting TypeSigs From <param name="Method"/> Sig.
            foreach (var _tsig in Method.Signature.ParameterTypes)
                _params.Add(_tsig);
            // If Method Is HasThis Insert Object Sig.
            if (Method.Signature.HasThis && !IsNewobj) _params.Insert(0, _importer.ImportTypeSignature(Method.Resolve().Parameters.ThisParameter.ParameterType));
            // Finally Return Maded Sig.
            return MethodSignature.CreateStatic(_returntype, _params);
        }
        /// <summary>
        /// Validating ProxyMethod CilBody.
        /// </summary>
        /// <param name="ProxyMethod"> New Maded Proxy Method. </param>
        /// <param name="CallInstruction"> Call Instruction That We Processing </param>
        private static void ProcessProxyBody(this MethodDefinition ProxyMethod, CilInstruction CallInstruction)
        {
            // Define MethodBody for ProxyMethod.
            ProxyMethod.CilMethodBody = new CilMethodBody(ProxyMethod);
            // Adding Args(Arguments) In Instructions.
            for (int x = 0; x < ProxyMethod.Parameters.Count; x++)
                ProxyMethod.CilMethodBody.Instructions.Add(CilOpCodes.Ldarg, ProxyMethod.Parameters[x]);
            // Add Proxied Method.
            ProxyMethod.CilMethodBody.Instructions.Add(CallInstruction.OpCode, (IMethodDescriptor)CallInstruction.Operand);
            // Add Ret-OpCode to Valided CilBody.
            ProxyMethod.CilMethodBody.Instructions.Add(CilOpCodes.Ret);
        }
    }
}
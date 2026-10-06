$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
public static class FamicomShaderCompiler {
    [ComImport,Guid("8BA5FB08-5195-40e2-AC58-0D989C3A0102"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface Blob {
        [PreserveSig] IntPtr GetBufferPointer();
        [PreserveSig] UIntPtr GetBufferSize();
    }
    [DllImport("d3dcompiler_47.dll",CallingConvention=CallingConvention.StdCall,CharSet=CharSet.Ansi)]
    static extern int D3DCompile(byte[] source,UIntPtr size,string name,IntPtr defines,IntPtr include,string entry,string target,uint flags,uint flags2,out Blob code,out Blob errors);
    public static void Compile(string sourcePath,string targetPath) {
        byte[] source=File.ReadAllBytes(sourcePath);Blob code,errors;
        int result=D3DCompile(source,(UIntPtr)source.Length,sourcePath,IntPtr.Zero,IntPtr.Zero,"main","ps_2_0",32768,0,out code,out errors);
        try {
            if(result<0)throw new Exception(errors==null?"Shader compilation failed":Marshal.PtrToStringAnsi(errors.GetBufferPointer()));
            byte[] bytes=new byte[(int)code.GetBufferSize().ToUInt64()];
            Marshal.Copy(code.GetBufferPointer(),bytes,0,bytes.Length);File.WriteAllBytes(targetPath,bytes);
        } finally { if(code!=null)Marshal.ReleaseComObject(code);if(errors!=null)Marshal.ReleaseComObject(errors); }
    }
}
'@
[FamicomShaderCompiler]::Compile((Join-Path $root 'native/Effects/CartridgeCrt.hlsl'),(Join-Path $root 'native/Effects/CartridgeCrt.ps'))
Get-Item -LiteralPath (Join-Path $root 'native/Effects/CartridgeCrt.ps') | Select-Object Name,Length
[FamicomShaderCompiler]::Compile((Join-Path $root 'native/Effects/HardwareFinish.hlsl'),(Join-Path $root 'native/Effects/HardwareFinish.ps'))
Get-Item -LiteralPath (Join-Path $root 'native/Effects/HardwareFinish.ps') | Select-Object Name,Length

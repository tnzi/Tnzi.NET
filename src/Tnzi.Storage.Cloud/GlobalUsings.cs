global using System.Net;

global using Amazon;
global using Amazon.S3;
global using Amazon.S3.Model;
global using Azure;
global using Azure.Storage.Blobs;
global using Azure.Storage.Blobs.Models;
global using Azure.Storage.Sas;

global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;

global using Tnzi.Modules;
global using Tnzi.Storage.Cloud.Providers;
global using Tnzi.Storage.Options;
global using Tnzi.Storage.Providers;
global using Tnzi.Utilities;

// 与 Tnzi.Storage 的别名逐字一致：这里的「文件不存在」是框架的业务异常
// （带 FilePath 与错误码），不是 System.IO.FileNotFoundException。
// 少了这一行代码照样编译，抛出的却换成了另一个类型，上层的异常映射随之失灵。
global using FileNotFoundException = Tnzi.Storage.Exceptions.FileNotFoundException;

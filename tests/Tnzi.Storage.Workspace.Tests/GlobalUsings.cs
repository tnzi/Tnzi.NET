global using System;
global using System.Collections.Generic;
global using System.IO;
global using System.Linq;
global using System.Text;
global using System.Threading;
global using System.Threading.Tasks;

global using Microsoft.EntityFrameworkCore;
global using Microsoft.Extensions.Configuration;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Logging.Abstractions;
global using Microsoft.Extensions.Options;
global using Moq;

global using Tnzi.Domain.Repositories;
global using Tnzi.EFCore;
global using Tnzi.Security.Claims;
global using Tnzi.TestBase;

global using Tnzi.Storage.Dtos;
global using Tnzi.Storage.Entities;
global using Tnzi.Storage.Options;
global using Tnzi.Storage.Providers;
global using Tnzi.Storage.Sanitization;
global using Tnzi.Storage.Services;

global using Tnzi.Storage.Workspace.Dtos;
global using Tnzi.Storage.Workspace.Entities;
global using Tnzi.Storage.Workspace.Services;
global using Tnzi.Storage.Workspace.Tests.TestSupport;

// 与被测程序集的别名逐字一致，理由见 src/Tnzi.Storage.Workspace/GlobalUsings.cs：
// 少了这几行代码仍然编译，`FileShare` 却指向 System.IO 的那一个。
global using FileChunk = Tnzi.Storage.Workspace.Entities.FileChunk;
global using FileShare = Tnzi.Storage.Workspace.Entities.FileShare;
global using FileUploadSession = Tnzi.Storage.Workspace.Entities.FileUploadSession;

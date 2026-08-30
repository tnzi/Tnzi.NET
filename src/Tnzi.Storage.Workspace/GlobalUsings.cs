global using System;
global using System.Collections.Generic;
global using System.ComponentModel.DataAnnotations;
global using System.IO;
global using System.Buffers.Text;
global using System.Linq;
global using System.Security.Cryptography;
global using System.Text;
global using System.Threading;
global using System.Threading.Tasks;

global using Microsoft.AspNetCore.Mvc;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Metadata.Builders;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;

global using Tnzi.Application;
global using Tnzi.AspNetCore.Extensions;
global using Tnzi.AspNetCore.Models;
global using Tnzi.AspNetCore.Mvc;
global using Tnzi.Data;
global using Tnzi.Domain.Entities;
global using Tnzi.Domain.Repositories;
global using Tnzi.EFCore;
global using Tnzi.EFCore.Internal;
global using Tnzi.EventBus;
global using Tnzi.Exceptions;
global using Tnzi.Extensions;
global using Tnzi.Security;
global using Tnzi.Modules;
global using Tnzi.MultiTenancy;
global using Tnzi.Results;
global using Tnzi.Security.Authorization;
global using Tnzi.Utilities;

global using Tnzi.Storage.Dtos;
// 父模块的核心实体（FileRecord / FileReference）—— 本模块的四个服务都要读写它们，
// 这正是「子引用父」的方向。父模块反过来对本模块的实体一无所知。
global using Tnzi.Storage.Entities;
global using Tnzi.Storage.Exceptions;
global using Tnzi.Storage.Helpers;
global using Tnzi.Storage.Options;
global using Tnzi.Storage.Permissions;
global using Tnzi.Storage.Providers;
global using Tnzi.Storage.Sanitization;
global using Tnzi.Storage.Services;

global using Tnzi.Storage.Workspace.Dtos;
global using Tnzi.Storage.Workspace.Entities;
global using Tnzi.Storage.Workspace.Events;
global using Tnzi.Storage.Workspace.Services;

// 与父模块 Tnzi.Storage 的别名同一条理由：这三个类型名都与 BCL 撞车。
// 少了这几行代码<b>仍然编译</b>，指向的却换成了 System.IO 里的那一个 ——
// `FileShare.None`（分片合并用的文件共享模式）与 `FileShare` 实体长得一模一样。
global using FileChunk = Tnzi.Storage.Workspace.Entities.FileChunk;
global using FileShare = Tnzi.Storage.Workspace.Entities.FileShare;
global using FileUploadSession = Tnzi.Storage.Workspace.Entities.FileUploadSession;

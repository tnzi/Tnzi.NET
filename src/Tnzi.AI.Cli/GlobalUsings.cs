// System
global using System;
global using System.Collections.Concurrent;
global using System.Collections.Generic;
global using System.Diagnostics;
global using System.IO;
global using System.Linq;
global using System.Runtime.CompilerServices;
global using System.Runtime.InteropServices;
global using System.Security.Cryptography;
global using System.Text;
global using System.Text.Json;
global using System.Text.Json.Nodes;
global using System.Text.Json.Serialization;
global using System.Threading;
global using System.Threading.Channels;
global using System.Threading.Tasks;

// Microsoft
global using Microsoft.AspNetCore.Mvc;
global using Microsoft.EntityFrameworkCore;
global using Microsoft.EntityFrameworkCore.Metadata.Builders;
global using Microsoft.Extensions.DependencyInjection;
global using Microsoft.Extensions.Hosting;
global using Microsoft.Extensions.Logging;
global using Microsoft.Extensions.Options;

// Tnzi framework
global using Tnzi.Application;
global using Tnzi.AspNetCore.Extensions;
global using Tnzi.AspNetCore.Models;
global using Tnzi.AspNetCore.Mvc;
global using Tnzi.Data;
global using Tnzi.Domain.Entities;
global using Tnzi.Domain.Repositories;
global using Tnzi.EFCore;
global using Tnzi.EFCore.Extensions;
global using Tnzi.EFCore.Internal;
global using Tnzi.Exceptions;
global using Tnzi.Extensions;
global using Tnzi.Json;
global using Tnzi.Mapster;
global using Tnzi.Modules;
global using Tnzi.MultiTenancy;
global using Tnzi.Options;
global using Tnzi.Results;
global using Tnzi.Security.Authorization;
global using Tnzi.Settings;
global using Tnzi.Utilities;

// AI core：契约、DTO、枚举全部住在核心，子模块只提供实现
global using Tnzi.AI.Dtos;
global using Tnzi.AI.Entities;
global using Tnzi.AI.Metadata;
global using Tnzi.AI.Services;
global using Tnzi.AI.Infrastructure.Streaming;
global using ErrorCodes = Tnzi.AI.Metadata.ErrorCodes;

// 本模块
global using Tnzi.AI.Cli.Adapters;
global using Tnzi.AI.Cli.Dispatch;
global using Tnzi.AI.Cli.Entities;
global using Tnzi.AI.Cli.Hosting;
global using Tnzi.AI.Cli.Metadata;
global using Tnzi.AI.Cli.Options;
global using Tnzi.AI.Cli.Permissions;
global using Tnzi.AI.Cli.Registry;
global using Tnzi.AI.Cli.Services;
global using Tnzi.AI.Cli.Workspace;

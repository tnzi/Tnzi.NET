global using System;
global using System.Collections.Generic;
global using System.Linq;
global using System.Threading.Tasks;

global using Microsoft.EntityFrameworkCore;

global using Moq;
global using Shouldly;

global using Tnzi.Authorization.DataAuth.Entities;
global using Tnzi.Authorization.DataAuth.Permissions;
global using Tnzi.Authorization.DataAuth.Services;
global using Tnzi.Domain.Repositories;
global using Tnzi.EFCore;
global using Tnzi.Identity.Services;
global using Tnzi.Security.Authorization;
global using Tnzi.TestBase;

global using Xunit;
global using Tnzi.Authorization.DataAuth.Extensions;
global using Tnzi.Security.Claims;

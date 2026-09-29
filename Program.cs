using FluentValidation;
using Microsoft.EntityFrameworkCore;
using SubscriptionManagement.Features.Subscriptions.CancelSubscriptions;
using SubscriptionManagement.Features.Subscriptions.CreateSubscriptions;
using SubscriptionManagement.Features.Subscriptions.GetSubscription;
using SubscriptionManagement.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(opt =>
    opt.UseInMemoryDatabase("subscriptions-db"));

builder.Services.AddValidatorsFromAssemblyContaining<CreateSubscriptionValidator>();

var app = builder.Build();

CreateSubscriptionEndpoint.Map(app);
CancelSubscriptionEndpoint.Map(app);
GetSubscriptionEndpoint.Map(app);

app.Run();
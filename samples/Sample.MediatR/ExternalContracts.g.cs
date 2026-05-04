using MediatR;

namespace Sample.MediatR.Contracts;

public sealed record ExternalOrderQuery(int OrderId) : IRequest<Sample.MediatR.OrderDto>;

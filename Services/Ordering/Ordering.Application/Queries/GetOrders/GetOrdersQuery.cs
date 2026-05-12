using BuildingBlocks.Pagination;

namespace Ordering.Application.Queries.GetOrders
{
    public record GetOrdersQuery(PaginationRequest PaginationRequest)
        : IQuery<GetOrdersResult>;

    public record GetOrdersResult(PaginatedResult<OrderDto> Orders);
}

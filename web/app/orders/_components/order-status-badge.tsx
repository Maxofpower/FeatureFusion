import { Badge } from "@/components/ui/badge"

export const ORDER_STATUS_COLORS: Record<string, string> = {
    Pending: "bg-amber-500/10 text-amber-600 dark:text-amber-400",
    Placed: "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400",
    Cancelled: "bg-destructive/10 text-destructive dark:bg-destructive/20",
    PaymentFailed: "bg-destructive/10 text-destructive dark:bg-destructive/20",
}

export const OrderStatusBadge = ({ status }: { status: string }) => (
    <Badge
        variant="ghost"
        className={ORDER_STATUS_COLORS[status] ?? "bg-muted text-muted-foreground"}
    >
        {status}
    </Badge>
)
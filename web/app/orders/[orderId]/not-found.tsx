import Link from "next/link"
import { buttonVariants } from "@/components/ui/button"
import { Card } from "@/components/ui/card"
import { APP_ROUTES } from "@/lib/constants/routes"
import { ReceiptText } from "lucide-react"

export default function OrderNotFound() {
  return (
    <div className="bg-background">
      <main className="px-4 py-8 sm:px-6 lg:px-8 flex flex-col gap-4">
        <Card className="flex flex-col items-center justify-center gap-3 p-12 text-center">
          <ReceiptText className="mb-1 size-8 text-muted-foreground/50" />
          <p className="text-lg font-medium text-foreground">Order not found</p>
          <p className="max-w-sm text-sm text-muted-foreground">
            The order you are looking for does not exist or may have been removed.
          </p>
          <Link href={APP_ROUTES.ORDERS} className={buttonVariants({ variant: "outline" })}>
            Back to orders
          </Link>
        </Card>
      </main>
    </div>
  )
}
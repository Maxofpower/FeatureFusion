import Link from "next/link"
import { buttonVariants } from "@/components/ui/button"
import { Card } from "@/components/ui/card"
import { APP_ROUTES } from "@/lib/constants/routes"
import { PackageX } from "lucide-react"

export default function ProductNotFound() {
  return (
    <div className="bg-background">
      <main className="px-4 py-8 sm:px-6 lg:px-8 flex flex-col gap-4">
        <Card className="flex flex-col items-center justify-center gap-3 p-12 text-center">
          <PackageX className="mb-1 size-8 text-muted-foreground/50" />
          <p className="text-lg font-medium text-foreground">Product not found</p>
          <p className="max-w-sm text-sm text-muted-foreground">
            The product you are looking for does not exist or may have been removed from the
            catalog.
          </p>
          <Link href={APP_ROUTES.CATALOG} className={buttonVariants({ variant: "outline" })}>
            Back to catalog
          </Link>
        </Card>
      </main>
    </div>
  )
}

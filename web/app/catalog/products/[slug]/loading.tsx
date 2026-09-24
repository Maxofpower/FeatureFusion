import { Separator } from "@/components/ui/separator"
import { Skeleton } from "@/components/ui/skeleton"

export default function Loading() {
  return (
    <div className="bg-background">
      <main className="px-4 py-8 sm:px-6 lg:px-8 flex flex-col gap-6">
        <Skeleton className="h-5 w-64" />

        <div className="grid gap-x-8 gap-y-6 lg:grid-cols-2">
          <div className="flex flex-col gap-4">
            <Skeleton className="aspect-square rounded-[1.25rem]" />
            <div className="grid grid-cols-5 gap-3">
              {Array.from({ length: 5 }).map((_, index) => (
                <Skeleton key={index} className="aspect-square rounded-xl" />
              ))}
            </div>
          </div>

          <div className="flex flex-col gap-4">
            <Skeleton className="h-4 w-48" />
            <Skeleton className="h-9 w-3/4" />
            <Skeleton className="h-4 w-40" />
            <Skeleton className="h-8 w-32" />
            <Skeleton className="h-5 w-44" />
            <Skeleton className="h-16 w-full" />
            <div className="flex gap-2">
              <Skeleton className="h-9 flex-1" />
              <Skeleton className="h-9 w-24" />
            </div>
            <Separator />
            <Skeleton className="h-6 w-40" />
            <Skeleton className="h-40 w-full" />
          </div>
        </div>
      </main>
    </div>
  )
}

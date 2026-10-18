import { Skeleton } from "@/components/ui/skeleton"

export default function Loading() {
    return (
        <div className="bg-background">
            <main className="px-4 py-8 sm:px-6 lg:px-8 flex flex-col gap-6">
                <Skeleton className="h-5 w-40" />

                <div className="flex items-center gap-4">
                    <Skeleton className="size-14 rounded-full" />
                    <div className="flex flex-col gap-2">
                        <div className="flex items-center gap-3">
                            <Skeleton className="h-8 w-44" />
                            <Skeleton className="h-5 w-14" />
                        </div>
                        <Skeleton className="h-4 w-56" />
                    </div>
                </div>

                <div className="grid gap-6 lg:grid-cols-3">
                    <div className="flex flex-col gap-4 lg:col-span-2">
                        <Skeleton className="h-6 w-24" />
                        <Skeleton className="h-4 w-64" />
                        <div className="rounded-xl ring-1 ring-foreground/10">
                            <Skeleton className="h-11 w-full rounded-t-xl" />
                            {Array.from({ length: 3 }).map((_, index) => (
                                <Skeleton key={index} className="h-12 w-full border-t border-border" />
                            ))}
                        </div>
                    </div>

                    <div className="flex flex-col gap-6">
                        <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
                            <Skeleton className="h-6 w-24" />
                            <div className="mt-3 flex flex-col gap-3">
                                <Skeleton className="h-4 w-full" />
                                <Skeleton className="h-4 w-full" />
                                <Skeleton className="h-4 w-2/3" />
                                <Skeleton className="h-4 w-3/4" />
                            </div>
                        </div>
                        <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
                            <Skeleton className="h-6 w-28" />
                            <div className="mt-3 flex flex-col gap-3">
                                <Skeleton className="h-4 w-full" />
                                <Skeleton className="h-4 w-full" />
                                <Skeleton className="h-4 w-1/2" />
                            </div>
                        </div>
                    </div>
                </div>
            </main>
        </div>
    )
}

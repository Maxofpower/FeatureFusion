import { Skeleton } from "@/components/ui/skeleton"

export default function Loading() {
    return (
        <div className="bg-background">
            <main className="px-4 py-8 sm:px-6 lg:px-8 flex flex-col gap-6">
                <Skeleton className="h-5 w-48" />

                <div className="flex flex-wrap items-start justify-between gap-4">
                    <div className="flex flex-col gap-2">
                        <div className="flex items-center gap-3">
                            <Skeleton className="h-8 w-44" />
                            <Skeleton className="h-5 w-20" />
                        </div>
                        <Skeleton className="h-4 w-64" />
                    </div>
                    <Skeleton className="h-8 w-36" />
                </div>

                <div className="grid gap-6 lg:grid-cols-3">
                    <div className="flex flex-col gap-4 lg:col-span-2">
                        <Skeleton className="h-6 w-24" />
                        <Skeleton className="h-4 w-48" />
                        <div className="rounded-xl ring-1 ring-foreground/10">
                            <Skeleton className="h-11 w-full rounded-t-xl" />
                            {Array.from({ length: 4 }).map((_, index) => (
                                <Skeleton key={index} className="h-12 w-full border-t border-border" />
                            ))}
                        </div>
                    </div>

                    <div className="flex flex-col gap-6">
                        <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
                            <Skeleton className="h-6 w-28" />
                            <div className="mt-3 flex flex-col gap-3">
                                <Skeleton className="h-4 w-full" />
                                <Skeleton className="h-4 w-full" />
                                <Skeleton className="h-4 w-2/3" />
                            </div>
                        </div>
                        <div className="rounded-xl bg-card p-4 ring-1 ring-foreground/10">
                            <Skeleton className="h-6 w-28" />
                            <div className="mt-3 flex flex-col gap-3">
                                <Skeleton className="h-4 w-full" />
                                <Skeleton className="h-4 w-full" />
                                <Skeleton className="h-4 w-full" />
                                <Skeleton className="h-6 w-32 self-end" />
                            </div>
                        </div>
                    </div>
                </div>
            </main>
        </div>
    )
}
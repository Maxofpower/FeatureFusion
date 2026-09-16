'use client'

import { useRouter, useSearchParams } from 'next/navigation'
import { useCallback, useState } from 'react'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
  SelectLabel,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import {
  Drawer,
  DrawerClose,
  DrawerContent,
  DrawerDescription,
  DrawerFooter,
  DrawerHeader,
  DrawerTitle,
  DrawerTrigger,
} from '@/components/ui/drawer'
import { SlidersHorizontal } from 'lucide-react'
import { useIsMobile } from "@/hooks/use-mobile"

interface Filters {
  page: string
  pageSize: string
  sortBy: string
  sortDirection: string
}

interface Props {
  currentFilters: Filters
}

// const SORT_OPTIONS = ['Id', 'Name', 'Price', 'CreatedAt'] as const
// const DIRECTION_OPTIONS = ['Ascending', 'Descending'] as const

const SORT_OPTIONS = [
  { label: "Id", value: "Id" },
  { label: "Name", value: "Name" },
  { label: "Price", value: "Price" },
  { label: "CreatedAt", value: "CreatedAt" }
]

const DIRECTION_OPTIONS = [
  { label: "Ascending", value: "Ascending" },
  { label: "Descending", value: "Descending" },
]

export const CatalogFilters = ({ currentFilters }: Props) => {
  const router = useRouter()
  const isMobile = useIsMobile()
  const [open, setOpen] = useState(false)

  const [page, setPage] = useState(currentFilters.page)
  const [pageSize, setPageSize] = useState(currentFilters.pageSize)
  const [sortBy, setSortBy] = useState(currentFilters.sortBy)
  const [sortDirection, setSortDirection] = useState(currentFilters.sortDirection)

  const applyFilters = useCallback(() => {
    const params = new URLSearchParams()
    params.set('page', page)
    params.set('pageSize', pageSize)
    params.set('sortBy', sortBy)
    params.set('sortDirection', sortDirection)
    router.push(`/catalog?${params.toString()}`)
    setOpen(false)
  }, [page, pageSize, sortBy, sortDirection, router])

  const resetFilters = useCallback(() => {
    setPage('1')
    setPageSize('24')
    setSortBy('Id')
    setSortDirection('Ascending')
    router.push('/catalog')
    setOpen(false)
  }, [router])

  return (
    <Drawer
      open={open}
      onOpenChange={setOpen}
      swipeDirection={isMobile ? "down" : "right"}
    >
      <DrawerTrigger render={<Button variant="outline" size="sm" />}>
        <SlidersHorizontal className="size-4" />
        Filters
      </DrawerTrigger>
      <DrawerContent>
        <DrawerHeader>
          <div className="flex items-center justify-between">
            <DrawerTitle>Catalog Filters</DrawerTitle>
            <Button variant="ghost" size="sm" onClick={resetFilters}>
              Reset
            </Button>
          </div>
          <DrawerDescription>
            Customize pagination, sort field, and sort direction. Changes apply server-side on submit.
          </DrawerDescription>
        </DrawerHeader>

        <div className="flex flex-col gap-4 overflow-y-auto px-4 py-2">

          <div className="flex flex-col gap-1.5">
            <label htmlFor="page" className="text-sm font-medium text-foreground">
              Page
            </label>
            <Input
              id="page"
              type="number"
              min={1}
              value={page}
              onChange={(e) => setPage(e.target.value)}
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <label htmlFor="pageSize" className="text-sm font-medium text-foreground">
              Page Size
            </label>
            <Input
              id="pageSize"
              type="number"
              min={1}
              max={100}
              value={pageSize}
              onChange={(e) => setPageSize(e.target.value)}
            />
          </div>

          <div className="flex flex-col gap-1.5">
            <label className="text-sm font-medium text-foreground">
              Sort By
            </label>
            <Select items={SORT_OPTIONS} value={sortBy} onValueChange={(value) => setSortBy(value || 'Id')}>
              <SelectTrigger className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  {SORT_OPTIONS.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectGroup>
              </SelectContent>
            </Select>
          </div>

          <div className="flex flex-col gap-1.5">
            <label className="text-sm font-medium text-foreground">
              Sort By
            </label>
            <Select items={DIRECTION_OPTIONS} value={sortDirection} onValueChange={(value) => setSortDirection(value || 'Ascending')}>
              <SelectTrigger className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                <SelectGroup>
                  {DIRECTION_OPTIONS.map((item) => (
                    <SelectItem key={item.value} value={item.value}>
                      {item.label}
                    </SelectItem>
                  ))}
                </SelectGroup>
              </SelectContent>
            </Select>
          </div>
        </div>
        <DrawerFooter>
          <div className="flex gap-2">
            <DrawerClose render={<Button variant="outline" className="flex-1" />}>
              Cancel
            </DrawerClose>
            <Button onClick={applyFilters} className="flex-1">
              Apply Filters
            </Button>
          </div>
        </DrawerFooter>
      </DrawerContent>
    </Drawer>
  )
}

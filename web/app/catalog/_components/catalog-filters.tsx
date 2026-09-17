'use client'

import { useRouter } from 'next/navigation'
import { useEffect, useMemo, useState } from 'react'
import { FormikErrors, useFormik } from 'formik'
import * as yup from 'yup'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectGroup,
  SelectItem,
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
import { useIsMobile } from '@/hooks/use-mobile'
import { FieldError } from '@/helper/field-error'

interface Filters {
  page: string
  pageSize: string
}

interface Props {
  currentFilters: Filters
  totalCount: number
}

type IFormState = Filters 

const DEFAULT_FILTERS: IFormState = {
  page: '1',
  pageSize: '24',
}

const MAX_PAGE_SIZE = 48

export const CatalogFilters = ({ currentFilters, totalCount }: Props) => {
  const router = useRouter()
  const isMobile = useIsMobile()
  const [open, setOpen] = useState(false)

  const initialValues: IFormState = {
    page: currentFilters.page,
    pageSize: currentFilters.pageSize,
  }

  const validationSchema = useMemo(
    () =>
      yup.object({
        page: yup
          .number()
          .typeError('Must be a number')
          .integer('Must be a whole number')
          .min(1, 'Must be at least 1')
          .required('This field is required')
          .test('max-page', function (value) {
            const pageSize = Number(this.parent.pageSize) || 1
            const maxPage = Math.max(1, Math.ceil(totalCount / pageSize))
            if (value === undefined || value <= maxPage) return true
            return this.createError({ message: `Must be at most ${maxPage}` })
          }),
        pageSize: yup
          .number()
          .typeError('Must be a number')
          .integer('Must be a whole number')
          .min(1, 'Must be at least 1')
          .max(MAX_PAGE_SIZE, `Must be at most ${MAX_PAGE_SIZE}`)
          .required('This field is required'),
      }),
    [totalCount]
  )

  const actionSubmit = (values: IFormState) => {
    const params = new URLSearchParams()
    params.set('page', values.page)
    params.set('pageSize', values.pageSize)
    router.push(`/catalog?${params.toString()}`)
    setOpen(false)
  }

  const formik = useFormik({
    initialValues,
    validationSchema,
    enableReinitialize: false,
    onSubmit: actionSubmit,
  })

  const formErrors: FormikErrors<IFormState> = {
    page: formik.submitCount || formik.touched.page ? formik.errors.page : '',
    pageSize: formik.submitCount || formik.touched.pageSize ? formik.errors.pageSize : ''
  }

  const handleDrawerChange = (nextOpen: boolean) => {
    setOpen(nextOpen)
    if (!nextOpen) {
      formik.resetForm()
    }
  }

  const resetFilters = () => {
    formik.resetForm({ values: DEFAULT_FILTERS })
    router.push('/catalog')
    setOpen(false)
  }

  useEffect(() => {
    if (open) {
      formik.setValues({
        page: currentFilters.page,
        pageSize: currentFilters.pageSize,
      })
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, currentFilters])

  return (
    <Drawer
      open={open}
      onOpenChange={handleDrawerChange}
      swipeDirection={isMobile ? 'down' : 'right'}
    >
      <DrawerTrigger render={<Button variant="outline" size="sm" />}>
        <SlidersHorizontal className="size-4" />
        Query Settings
      </DrawerTrigger>
      <DrawerContent>
        <form onSubmit={formik.handleSubmit} className="flex flex-col h-full">
          <DrawerHeader>
            <div className="flex items-center justify-between">
              <DrawerTitle>Catalog Query Settings</DrawerTitle>
              <Button variant="ghost" size="sm" type="button" onClick={resetFilters}>
                Reset
              </Button>
            </div>
            <DrawerDescription>
              Customize pagination and page size.
            </DrawerDescription>
          </DrawerHeader>

          <div className="flex flex-col gap-4 overflow-y-auto px-4 py-2">
            <div className="space-y-2">
              <Label htmlFor="page" className="text-sm font-medium text-foreground">
                Page
              </Label>
              <Input
                id="page"
                name="page"
                type="number"
                min={1}
                value={formik.values.page}
                onChange={formik.handleChange}
                onBlur={formik.handleBlur}
                aria-invalid={!!formErrors.page}
              />
              <FieldError message={formErrors.page} />
            </div>

            <div className="space-y-2">
              <Label htmlFor="pageSize" className="text-sm font-medium text-foreground">
                Page Size
              </Label>
              <Input
                id="pageSize"
                name="pageSize"
                type="number"
                min={1}
                max={MAX_PAGE_SIZE}
                value={formik.values.pageSize}
                onChange={formik.handleChange}
                onBlur={formik.handleBlur}
                aria-invalid={!!formErrors.pageSize}
              />
              <FieldError message={formErrors.pageSize} />
            </div>

          </div>

          <DrawerFooter>
            <div className="flex gap-2">
              <DrawerClose render={<Button variant="outline" type="button" className="flex-1" />} onClick={() => formik.resetForm()}>
                Cancel
              </DrawerClose>
              <Button type="submit" className="flex-1">
                Apply Filters
              </Button>
            </div>
          </DrawerFooter>
        </form>
      </DrawerContent>
    </Drawer>
  )
}